using System.IdentityModel.Tokens.Jwt;
using CryptoCrush.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoCrush.Services.Token;

public class UserIdentity(IOptions<EnvVariables> env) : IUserIdentity
{
    private readonly EnvVariables _env = env.Value;
    public string GetAddressByCookie(HttpContext context)
    {
        var cookie = context.Request.Cookies;

        if (!cookie.TryGetValue(_env.CookieName, out var address))
            return string.Empty;

        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(address);

            return jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value
                   ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}