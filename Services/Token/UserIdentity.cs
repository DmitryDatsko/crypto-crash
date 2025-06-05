using System.IdentityModel.Tokens.Jwt;
using CryptoCrush.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoCrush.Services.Token;

public class UserIdentity : IUserIdentity
{
    private readonly IHttpContextAccessor _accessor;
    private readonly EnvVariables _envVariables;

    public UserIdentity(IHttpContextAccessor accessor, IOptions<EnvVariables> envVariables)
    {
        _accessor = accessor;
        _envVariables = envVariables.Value;
    }

    private bool HasWalletCookie()
    {
        var cookies = _accessor.HttpContext?.Request.Cookies;
        return cookies != null && cookies.ContainsKey(_envVariables.CookieName);
    }

    public string GetAddressByCookie()
    {
        if (!HasWalletCookie()) 
            return string.Empty;

        var cookies = _accessor.HttpContext!.Request.Cookies;
        if (!cookies.TryGetValue(_envVariables.CookieName, out var token))
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


