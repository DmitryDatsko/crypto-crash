using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json.Linq;

namespace CryptoCrush.Services.Hub;

public class CryptoPriceService(ILogger<CryptoPriceService> logger): BackgroundService
{
    private ClientWebSocket _client = null!;
    private static readonly Uri Uri = new("wss://stream.bybit.com/v5/public/spot");
    private const string Topic = "tickers.BTCUSDT";
    private decimal _price;
    public decimal Price
    {
        get => _price;
        private set
        {
            _price = value;
            OnPriceChanged?.Invoke(this, value);
        }
    }
    public event EventHandler<decimal>? OnPriceChanged;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = TimeSpan.FromSeconds(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _client?.Dispose();
                _client = new ClientWebSocket
                {
                    Options = { KeepAliveInterval = TimeSpan.FromSeconds(30) }
                };
                await _client.ConnectAsync(Uri, stoppingToken);

                var subscribeMsg = Encoding.UTF8.GetBytes($@"{{
                    ""op"": ""subscribe"",
                    ""args"": [""{Topic}""]
                }}");

                await _client.SendAsync(subscribeMsg, WebSocketMessageType.Text, true, stoppingToken);

                backoff = TimeSpan.FromSeconds(1);

                var buffer = new byte[8192];
                while (_client.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    var result = await _client.ReceiveAsync(buffer, stoppingToken);
                    if (result.MessageType != WebSocketMessageType.Text)
                        continue;

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    JObject json = JObject.Parse(message);

                    if (json["topic"]?.ToString() == Topic &&
                        decimal.TryParse(json["data"]?["lastPrice"]?.ToString(),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out var lastPrice))
                    {
                        Price = lastPrice;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "WebSocket disconnected, reconnect in {Delay}s", backoff.TotalSeconds);
                await Task.Delay(backoff, stoppingToken);

                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 60));
            }
        }
        
        if(_client.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await _client.CloseAsync(WebSocketCloseStatus.NormalClosure,
                "Service stopping", CancellationToken.None);
        }
        _client.Dispose();
    }
}