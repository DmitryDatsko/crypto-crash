using System.IdentityModel.Tokens.Jwt;
using CryptoCrush.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoCrush.Services.Token;

public class UserIdentity(IHttpContextAccessor accessor, IOptions<EnvVariables> env) : IUserIdentity
{
    private readonly EnvVariables _env = env.Value;
    private bool HasWalletCookie()
    {
        var cookies = accessor.HttpContext?.Request.Cookies;
        
        return cookies != null && cookies.ContainsKey(_env.CookieName);
    }
    public string GetAddressByCookie()
    {
        if(!HasWalletCookie()) return string.Empty;
        
        var cookies = accessor.HttpContext?.Request.Cookies;
        if (cookies == null || !cookies.TryGetValue(_env.CookieName, out var token))
            return string.Empty;

        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value
                   ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}