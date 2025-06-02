namespace CryptoCrush.Models.DTO;

public class BetDto
{
    public required decimal Amount { get; set; }
    public required bool IsWinner { get; set; }
    public required string UserAddress { get; set; }
}