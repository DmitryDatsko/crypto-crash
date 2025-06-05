namespace CryptoCrush.Services.Token;

public interface IUserIdentity
{
    string GetAddressByCookie(HttpContext context);
}