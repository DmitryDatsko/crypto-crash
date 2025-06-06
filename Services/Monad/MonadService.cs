using System.Diagnostics;
using CryptoCrush.Configuration;
using CryptoCrush.Models;
using CryptoCrush.Models.Contract;
using CryptoCrush.Models.DTO;
using Microsoft.Extensions.Options;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;

namespace CryptoCrush.Services.Monad;
 
public class MonadService : IMonadService
{
    private readonly EnvVariables _env;
    private readonly Web3 _web3;
    private readonly Web3 _web3OwnerKey;
    private readonly int _requiredConfirmations;
    private readonly TimeSpan _mempoolTimeSec;
    private readonly ILogger<MonadService> _logger;

    public MonadService(IOptions<EnvVariables> env, ILogger<MonadService> logger)
    {
        _env = env.Value;
        _web3 = new(_env.RpcUrl);

        var account = new Account(_env.ContractOwnerPrivateKey);
        _web3OwnerKey = new(account, _env.RpcUrl);
        
        _mempoolTimeSec = TimeSpan.FromSeconds(_env.MempoolTimeSec);
        _requiredConfirmations = _env.RequiredConfirmations;
        
        _logger = logger;
    }
    private async Task<bool> OnConnected() => await _web3.Net.Version.SendRequestAsync() == _env.NetworkId;
    
    public async Task<TransactionReceipt?> VerifyTransactionAsync(
        TransactionRequest request, CancellationToken cancellationToken = default)
    {
        if (!await OnConnected())
        {
            _logger.LogError("not connected");
            return null;
        }
        
        var inMempool = await WaitForMempoolAsync(
            request.TransactionHash, cancellationToken);

        if (!inMempool)
        {
            _logger.LogError("not in mempool");
            return null;
        }
        
        var receipt = await _web3.Eth.Transactions
            .GetTransactionReceipt.SendRequestAsync(request.TransactionHash);
        
        if(!(string.Equals(receipt.From, request.AddressFrom, StringComparison.OrdinalIgnoreCase)
           && string.Equals(receipt.To, _env.ContractAddress, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogError("bad from/to address");
            return null;
        }

        var createdBlock = receipt.BlockNumber.Value;
        var latestBlock = (await _web3.Eth.Blocks.GetBlockNumber
            .SendRequestAsync()).Value;
            
        while (latestBlock - createdBlock <= _requiredConfirmations)
        {
            latestBlock = (await _web3.Eth.Blocks.GetBlockNumber
                .SendRequestAsync()).Value;
                
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
        }
        
        return receipt;
    }

    public async Task AddWinnersBalance(
        Dictionary<Bet, decimal> payouts, CancellationToken cancellationToken = default)
    {
        var handler = _web3OwnerKey.Eth.GetContractTransactionHandler<AddWinnersBalanceFunction>();

        foreach (var batchDict in ChuckDictionary(payouts, 200))
        {
            var users = batchDict.Select(kvp => new User
            {
                UserAddress = kvp.Key.UserAddress,
                Amount = Web3.Convert.ToWei(kvp.Value)
            }).ToList();

            var function = new AddWinnersBalanceFunction { Banch = users };
            
            var receipt = await handler
                .SendRequestAndWaitForReceiptAsync(
                    _env.ContractAddress,
                    function,
                    cancellationToken);

            if (receipt.Status.Value == 0)
            {
                _logger.LogError("bad winners balance");
                return;
            }
        }
    }
    
    private async Task<bool> WaitForMempoolAsync(string txHash, CancellationToken cancellationToken = default)
    {
        var stopWatch = Stopwatch.StartNew();
        do
        {
            var txn = await _web3.Eth.Transactions
                .GetTransactionByHash
                .SendRequestAsync(txHash);
            
            if(txn != null)
                return true;

            await Task.Delay(200, cancellationToken);
        } while (stopWatch.Elapsed < _mempoolTimeSec && !cancellationToken.IsCancellationRequested);
        
        return false;
    }
    
    private static IEnumerable<Dictionary<TKey, TValue>> ChuckDictionary<TKey, TValue>(
        Dictionary<TKey, TValue> source,
        int chunkSize) where TKey : notnull
    {
        var items = source.ToList();

        for (int i = 0; i < items.Count; i += chunkSize)
        {
            int size = Math.Min(chunkSize, items.Count - i);
            var slice = items.GetRange(i, size);
            
            yield return slice.ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }
}