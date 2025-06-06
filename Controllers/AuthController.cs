using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CryptoCrush.Configuration;
using CryptoCrush.Models.DTO;
using CryptoCrush.Services.Token;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nethereum.Signer;

namespace CryptoCrush.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IOptions<EnvVariables> env,
    IUserIdentity userIdentity) : ControllerBase
{
    private readonly EnvVariables _env = env.Value;

    [HttpPost("verify")]
    public IActionResult Authenticate([FromBody] AuthenticationRequest request)
    {
        var signer = new EthereumMessageSigner();
        
        var recoveredAddress = signer.EncodeUTF8AndEcRecover(request.Message, request.Signature);

        if (!string.IsNullOrEmpty(recoveredAddress))
        {
            var accessToken = CreateToken(recoveredAddress);
            
            if (!string.IsNullOrEmpty(accessToken))
            {
                SetCookie(accessToken, HttpContext);
                return Ok(new { message = "Authentication successful" });
            }
        }

        return Unauthorized(new { message = "Invalid signature" });
    }

    [HttpGet("nonce")]
    public IActionResult Nonce() => Ok(GenerateSecureNonce());
    
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var address = userIdentity.GetAddressByCookie(HttpContext);
        
        if (string.IsNullOrEmpty(address))
            return Unauthorized("Invalid or missing JWT cookie");
        
        return Ok(new { WalletAddress = address });
    }

    [Authorize]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        RemoveCookie(HttpContext);

        return Unauthorized();
    }
    
    private string CreateToken(string address)
    {
        var unixIat = new DateTimeOffset(DateTime.UtcNow)
            .ToUnixTimeSeconds().ToString();
        
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, address),
            new("address", address),
            new(JwtRegisteredClaimNames.Iat, unixIat, ClaimValueTypes.Integer64)
        };
        
        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(_env.JwtTokenSecret));
        
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(1),
            signingCredentials: credentials
        );
        
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        
        return jwt;
    }
    
    private void SetCookie(string accessToken, HttpContext httpContext)
    {
        httpContext.Response.Cookies.Append(_env.CookieName, accessToken,
            new CookieOptions
            {
                Path = "/",
                Secure = true,
                HttpOnly = true,
                Domain = "crypto-crush-api.duckdns.org",
                SameSite = SameSiteMode.None,
                Expires = DateTime.UtcNow.AddDays(1)
            });
    }
    private void RemoveCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(_env.CookieName, new CookieOptions
        {
            Path = "/",
            Domain = "crypto-crush-api.duckdns.org",
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.None,
            Expires = DateTimeOffset.UnixEpoch
        });
    }
    private static string GenerateSecureNonce(int length = 64)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var nonce = new char[length];
        using var rng = RandomNumberGenerator.Create();
        var buffer = new byte[sizeof(uint)];

        for (int i = 0; i < length; i++)
        {
            rng.GetBytes(buffer);
            uint num = BitConverter.ToUInt32(buffer, 0);
            nonce[i] = chars[(int)(num % (uint)chars.Length)];
        }

        return new string(nonce);
    }
}