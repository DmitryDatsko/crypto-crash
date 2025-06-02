namespace CryptoCrush.Models.DTO;

public class AuthenticationRequest
{
    public required string Message { get; set; }
    public required string Signature { get; set; }
}