using System.ComponentModel.DataAnnotations;

namespace CryptoCrush.Models;

public class Bet
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(6)] public string Side { get; set; } = string.Empty;
    [MaxLength(128)] public string UserAddress { get; set; } = string.Empty;
    public Guid GameId { get; set; }
    
    public Game Game { get; set; } = null!;
}