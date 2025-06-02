using System.Collections.Concurrent;
using System.Diagnostics;
using CryptoCrush.Configuration;
using CryptoCrush.Data;
using CryptoCrush.Enums;
using CryptoCrush.Hubs;
using CryptoCrush.Models;
using CryptoCrush.Models.DTO;
using CryptoCrush.Services.Monad;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CryptoCrush.Services.Hub;

public class BettingService : BackgroundService
{
    private readonly IDbContextFactory<ApiDbContext> _dbContextFactory;
    private readonly IHubContext<GameHub> _hub;
    private readonly IMonadService _monadService;
    private readonly CryptoPriceService _cryptoPriceService;
    private readonly ILogger<BettingService> _logger;
    private readonly object _lock = new();
    
    private readonly decimal _totalPixels;
    private readonly decimal _margin;

    private Stopwatch _gameStopwatch = new();
    private Guid _gameId;
    private bool _isBettingOpen;
    private bool _isGameStarted;
    private double _lastElapsedSeconds;
    private readonly TimeSpan _gameDuration;
    private readonly TimeSpan _betTime;
    private readonly TimeSpan _delayAfterGame;

    private readonly List<decimal> _prices = new();
    private readonly ConcurrentDictionary<PixelKey, decimal> _pixels = new();
    private readonly Dictionary<Bet, decimal> _payouts = new();
    
    public BettingService(
        IHubContext<GameHub> hub,
        CryptoPriceService cryptoPriceService,
        IDbContextFactory<ApiDbContext> dbContextFactory,
        IMonadService monadService,
        IOptions<EnvVariables> env,
        ILogger<BettingService> logger)
    {
        var envValue = env.Value;
        _hub = hub;
        _cryptoPriceService = cryptoPriceService;
        _dbContextFactory = dbContextFactory;
        _monadService = monadService;
        _logger = logger;

        _pixels[PixelKey.LongX] = 0m;
        _pixels[PixelKey.ShortX] = 0m;

        _totalPixels = envValue.TotalPixels;
        _margin = envValue.Margin;
        _gameDuration = TimeSpan.FromSeconds(envValue.GameDuration);
        _betTime = TimeSpan.FromSeconds(envValue.BetTime);
        _delayAfterGame = TimeSpan.FromSeconds(envValue.DelayAfterGame);
    }

    private void UpdateState(bool openBetting, bool gameStarted, Guid gameId)
    {
        lock (_lock)
        {
            _isBettingOpen = openBetting;
            _isGameStarted = gameStarted;
            _gameId = gameId;
        }
    }
    
    public BettingState ReadState()
    {
        lock (_lock)
        {
            return new BettingState
            {
                IsBettingOpen = _isBettingOpen,
                IsGameStarted = _isGameStarted,
                GameId = _gameId,
            };
        }
    }

    private void NormalizeFinalPixels(BetSides gameResult)
    {
        switch (gameResult)
        {
            case BetSides.Tie:
                if (_pixels[PixelKey.LongX] != _pixels[PixelKey.ShortX])
                {
                    _pixels[PixelKey.LongX] = Math.Max(_pixels[PixelKey.ShortX], _pixels[PixelKey.LongX]);
                    _pixels[PixelKey.ShortX] = _pixels[PixelKey.LongX];
                }
                break;
            case BetSides.Long:
                if (_pixels[PixelKey.LongX] <= _pixels[PixelKey.ShortX])
                {
                    _pixels[PixelKey.LongX] = Math.Clamp(_pixels[PixelKey.LongX], _pixels[PixelKey.ShortX] + 20, _totalPixels);
                }
                break;
            case BetSides.Short:
                if (_pixels[PixelKey.LongX] >= _pixels[PixelKey.ShortX])
                {
                    _pixels[PixelKey.ShortX] = Math.Clamp(_pixels[PixelKey.ShortX], _pixels[PixelKey.LongX] + 20, _totalPixels);
                }
                break;
        }
    }

    private async Task PayoutCalculator(decimal totalPool, List<Bet> winningBets)
    {
        if(totalPool <= 0 || !winningBets.Any())
            return;
        
        var totalWinningStake = winningBets.Sum(b => b.Amount);
        var loserPool = totalPool - totalWinningStake; 
        var poolForWinners = loserPool * (1 - _margin);

        foreach (var bet in winningBets)
        {
            decimal payout = bet.Amount;

            if (poolForWinners > 0 && totalWinningStake > 0)
            {
                var share = (payout / totalWinningStake) * poolForWinners;
                payout += share;
            }
            
            _payouts[bet] = payout;
        }
        
        await _monadService.AddWinnersBalance(_payouts);
    }
    
    private void ClearPixelsDictionary()
    {
        foreach (var key in _pixels.Keys)
        {
            _pixels[key] = 0m;
        }
    }
    
    private async Task RunCountdownAsync(Stopwatch stopwatch, double totalSeconds, string methodName, CancellationToken cancellationToken = default)
    {
        const double eps = 0.005;
        double lastSentTime = -1;

        while (stopwatch.Elapsed.TotalSeconds < totalSeconds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double elapsed = Math.Round(stopwatch.Elapsed.TotalSeconds, 2);

            if (Math.Abs(elapsed - lastSentTime) > eps)
            {
                lastSentTime = elapsed;
                await _hub.Clients.All.SendAsync(methodName, elapsed, cancellationToken: cancellationToken);
            }

            await Task.Delay(50, cancellationToken);
        }

        await _hub.Clients.All.SendAsync(methodName, totalSeconds, cancellationToken: cancellationToken);
    }
    
    private void HandlePriceChanged(object? sender, decimal newPrice)
    {
        _ = CarSpeedOnPriceChange(newPrice)
            .ContinueWith(t =>
            {
                if(t.Exception != null) _logger.LogCritical($"Exception in price change handler: {t.Exception}");
            }, TaskContinuationOptions.OnlyOnFaulted);
    }
    
    private async Task CarSpeedOnPriceChange(decimal newPrice)
    {
        _prices.Add(newPrice);

        if (_prices.Count > 2)
        {
            double elapsed = _gameStopwatch.Elapsed.TotalSeconds;
            double dt = elapsed - _lastElapsedSeconds;
            _lastElapsedSeconds = elapsed;
            
            decimal priceDelta = _prices[^1] - _prices[^2];
            decimal rawPx = Math.Abs(priceDelta);
            
            decimal maxStep = _totalPixels * (decimal)(dt / _gameDuration.TotalSeconds);
            decimal movePx = Math.Round(Math.Clamp(rawPx, 0, maxStep), 2);
            
            if(priceDelta > 0) _pixels[PixelKey.LongX] += movePx;
            if (priceDelta < 0) _pixels[PixelKey.ShortX] += movePx;
            
            await _hub.Clients.All.SendAsync(HubMethods.RaceTick, new
            {
                LongX = _pixels[PixelKey.LongX],
                ShortX = _pixels[PixelKey.ShortX]
            });
        }
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await using var db = await _dbContextFactory.CreateDbContextAsync(stoppingToken);
                UpdateState(true, false, Guid.CreateVersion7());
                
                var newGame = new Game
                {
                    Id = _gameId,
                    PlayedAt = DateTime.UtcNow,
                    WinningSide = string.Empty
                };
                
                await db.Games.AddAsync(newGame, stoppingToken);
                await db.SaveChangesAsync(stoppingToken);
                
                await _hub.Clients.All.SendAsync(HubMethods.BettingState, 
                    ReadState(),
                    cancellationToken: stoppingToken);
                
                var betStopwatch = Stopwatch.StartNew();
                await RunCountdownAsync(betStopwatch, _betTime.TotalSeconds, HubMethods.Timer, cancellationToken: stoppingToken);
                
                UpdateState(false, true, _gameId);
                
                await _hub.Clients.All.SendAsync(HubMethods.BettingState, 
                    ReadState(),
                    cancellationToken: stoppingToken);
                
                _gameStopwatch = Stopwatch.StartNew();
                _lastElapsedSeconds = 0.0;
                
                var timerTask = RunCountdownAsync(_gameStopwatch, _gameDuration.TotalSeconds, HubMethods.Timer, stoppingToken);
                
                _cryptoPriceService.OnPriceChanged += HandlePriceChanged;

                var frameIntervalMs = 100.0;
                var nextTickMs = frameIntervalMs;
                
                while (_gameStopwatch.Elapsed < _gameDuration)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    var elapsedMs = _gameStopwatch.Elapsed.TotalMilliseconds;
                    if (elapsedMs >= nextTickMs)
                    {
                        do
                        {
                            _pixels[PixelKey.LongX] += 0.15m;
                            _pixels[PixelKey.ShortX] += 0.15m;
                            nextTickMs += frameIntervalMs;
                        }
                        while (elapsedMs >= nextTickMs);
                        
                        await _hub.Clients.All.SendAsync(HubMethods.RaceTick, new
                        {
                            LongX  = _pixels[PixelKey.LongX],
                            ShortX = _pixels[PixelKey.ShortX]
                        }, stoppingToken);
                    }
                    
                    await Task.Delay(100, stoppingToken);
                }
                
                await timerTask;
                _cryptoPriceService.OnPriceChanged -= HandlePriceChanged;
                
                BetSides gameResult = _prices[^1] == _prices[0] ? BetSides.Tie :
                    _prices[^1] > _prices[0] ? BetSides.Long : BetSides.Short;
                
                newGame.WinningSide = gameResult.ToString();
                await db.SaveChangesAsync(stoppingToken);
                
                NormalizeFinalPixels(gameResult);
                await _hub.Clients.All.SendAsync(HubMethods.RaceTick, new
                {
                    LongX  = _pixels[PixelKey.LongX],
                    ShortX = _pixels[PixelKey.ShortX]
                }, stoppingToken);
                
                UpdateState(false, false, _gameId);
                var state = ReadState();
                
                var bets = await db.Bets
                    .AsNoTracking()
                    .Where(bet => bet.GameId == _gameId)
                    .Select(b => new BetDto
                    {
                        Amount = b.Amount,
                        IsWinner = Equals(b.Side == gameResult.ToString(), StringComparison.OrdinalIgnoreCase),
                        UserAddress = b.UserAddress
                    })
                    .ToListAsync(stoppingToken);

                await _hub.Clients.All.SendAsync(HubMethods.GameResult, new
                {
                    gameResult,
                    state.IsBettingOpen,
                    state.IsGameStarted,
                    bets
                }, stoppingToken);

                var winningBets = await db.Bets
                    .AsNoTracking()
                    .Where(b => b.GameId == _gameId &&
                                Equals(b.Side == gameResult.ToString(), StringComparison.OrdinalIgnoreCase))
                    .ToListAsync(stoppingToken);
                
                var totalPool = await db.Bets.AsNoTracking()
                    .Where(b => b.GameId == _gameId)
                    .SumAsync(b => b.Amount, stoppingToken);
                
                await PayoutCalculator(totalPool, winningBets);
                
                ClearPixelsDictionary();
                _prices.Clear();
                _payouts.Clear();
                await Task.Delay(_delayAfterGame, stoppingToken);
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogCritical($"Operation cancelled exception: {ex}");
        }
    }
}