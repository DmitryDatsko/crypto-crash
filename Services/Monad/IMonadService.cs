using CryptoCrush.Models;
using CryptoCrush.Models.DTO;
using Nethereum.RPC.Eth.DTOs;

namespace CryptoCrush.Services.Monad;

public interface IMonadService
{
    Task<TransactionReceipt?> VerifyTransactionAsync(TransactionRequest request, CancellationToken cancellationToken = default);
    Task AddWinnersBalance(Dictionary<Bet, decimal> payouts, CancellationToken cancellationToken = default);
}