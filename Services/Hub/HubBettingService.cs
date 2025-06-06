using System.Collections.ObjectModel;

namespace CryptoCrush.Services.Hub;

public class HubBettingService
{ 
    private readonly Dictionary<string, decimal> _bets = new();

    public void AddBet(string userAddress, decimal bet)
    {
        _bets[userAddress] = bet;
    }

    public IReadOnlyDictionary<string, decimal> GetAllBets()
    {
        return new ReadOnlyDictionary<string, decimal>(_bets);
    }
    
    public decimal GetBank() => _bets.Values.Sum();

    public void ClearBets()
    {
        _bets.Clear();
    }
}