namespace CryptoCrush.Models.DTO;

public class TransactionRequest
{
    public required string TransactionHash { get; set; }
    public required string AddressFrom { get; set; }
}