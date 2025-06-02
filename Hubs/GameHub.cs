using CryptoCrush.Data;
using CryptoCrush.Enums;
using CryptoCrush.Models;
using CryptoCrush.Models.DTO;
using CryptoCrush.Services.Hub;
using CryptoCrush.Services.Monad;
using CryptoCrush.Services.Token;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CryptoCrush.Hubs;

public sealed class GameHub(
    BettingService bettingService,
    IMonadService monadService,
    IUserIdentity userIdentity,
    IDbContextFactory<ApiDbContext> dbContextFactory) : Hub
{
    private readonly Dictionary<string, decimal> _bets = new();

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync(HubMethods.OnConnected, bettingService.ReadState());
        await base.OnConnectedAsync();
    }
    
    public async Task PlaceBet(
        Guid gameId,
        decimal amount,
        BetSides side,
        string txHash)
    {
        var gameState = bettingService.ReadState();
        
        if (gameId != gameState.GameId)
        {
            await Clients.User(Context.UserIdentifier ?? string.Empty)
                .SendAsync(HubMethods.BetRejected, "This game has already finished.");
            return;
        }
        
        if (side is not (BetSides.Long or BetSides.Short or BetSides.Tie))
        {
            await Clients.User(Context.UserIdentifier ?? string.Empty)
                .SendAsync(HubMethods.BetRejected, "Bets are closed now");
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var userAddress = userIdentity.GetAddressByCookie();
        bool isBetPlaced = await db.Bets
            .AsNoTracking()
            .AnyAsync(b =>
                b.GameId == gameState.GameId &&
                b.UserAddress == userAddress);
        
        if (isBetPlaced)
        {
            await Clients.User(Context.UserIdentifier ?? string.Empty).SendAsync(HubMethods.BetRejected, "Already placed");
            return;
        }

        var receipt = await monadService.VerifyTransactionAsync(new TransactionRequest
            { TransactionHash = txHash, AddressFrom = userAddress });
        
        if (receipt == null)
        {
            await Clients.User(Context.UserIdentifier ?? string.Empty).SendAsync(HubMethods.BetRejected, "Transaction not verified");
            return;
        }

        await db.Bets.AddAsync(new Bet
        {
            Id = Guid.CreateVersion7(),
            Amount = amount,
            Side = side.ToString(),
            UserAddress = userAddress,
            GameId = gameState.GameId
        });
        
        await db.SaveChangesAsync();
        
        await Clients.User(Context.UserIdentifier ?? string.Empty)
            .SendAsync(HubMethods.BetAccepted);
        
        _bets.Add(userAddress, amount);
        
        await Clients.All.SendAsync(HubMethods.Bets, new
        {
            bets = _bets,
            bank = _bets.Sum(b => b.Value)
        });
    }
}