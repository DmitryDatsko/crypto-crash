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
    ILogger<GameHub> logger,
    IDbContextFactory<ApiDbContext> dbContextFactory,
    HubBettingService hubBettingService) : Hub
{
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
            logger.LogCritical("Invalid game id");
            await Clients.User(Context.UserIdentifier ?? string.Empty)
                .SendAsync(HubMethods.BetRejected, "This game has already finished.");
            return;
        }
        
        if (side is not (BetSides.Long or BetSides.Short or BetSides.Tie))
        {
            logger.LogCritical("Bad side");
            await Clients.User(Context.UserIdentifier ?? string.Empty)
                .SendAsync(HubMethods.BetRejected, "Bets are closed now");
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var userAddress = userIdentity.GetAddressByCookie(Context.GetHttpContext()!);
        bool isBetPlaced = await db.Bets
            .AsNoTracking()
            .AnyAsync(b =>
                b.GameId == gameState.GameId &&
                b.UserAddress == userAddress);
        
        if (isBetPlaced)
        {
            logger.LogCritical("Already placed");
            await Clients.User(Context.UserIdentifier ?? string.Empty).SendAsync(HubMethods.BetRejected, "Already placed");
            return;
        }

        var receipt = await monadService.VerifyTransactionAsync(new TransactionRequest
            { TransactionHash = txHash, AddressFrom = userAddress });
        
        if (receipt == null)
        {
            logger.LogCritical("Transaction not verified");
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
        
        hubBettingService.AddBet(userAddress, amount);
        
        await Clients.All.SendAsync(HubMethods.Bets, new
        {
            bets = hubBettingService.GetAllBets(),
            bank = hubBettingService.GetBank()
        });
    }
}