using System.ComponentModel.DataAnnotations;

namespace CryptoCrush.Models;

public class Bet
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(6)] public string Side { get; set; }
    [MaxLength(128)] public string UserAddress { get; set; }
    public Guid GameId { get; set; }
    
    public Game Game { get; set; } = null!;
}