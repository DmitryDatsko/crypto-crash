using System.ComponentModel.DataAnnotations;

namespace CryptoCrush.Models;

public class Game
{
    public Guid Id { get; set; }
    [MaxLength(6)] public string WinningSide { get; set; } = string.Empty;
    public DateTime PlayedAt { get; set; }
    
    public ICollection<Bet> Bets { get; set; } = new List<Bet>();
}