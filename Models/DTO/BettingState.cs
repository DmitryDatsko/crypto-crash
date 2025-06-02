namespace CryptoCrush.Models.DTO;

public class BettingState
{
    public bool IsBettingOpen  { get; init; }
    public bool IsGameStarted { get; init; }
    public Guid GameId { get; init; }
}