using CryptoCrush.Configuration;
using CryptoCrush.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace CryptoCrush.Filters;

public class HubAuthorize(
    IOptions<EnvVariables> env,
    ILogger<HubAuthorize> logger) : IHubFilter
{
    private readonly EnvVariables _env = env.Value;

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var httpContext = invocationContext.Context.GetHttpContext();

        if (invocationContext.HubMethodName == nameof(GameHub.PlaceBet))
        {
            if (httpContext == null
                || !httpContext.Request.Cookies.ContainsKey(_env.CookieName))
            {
                logger.LogCritical("Invalid cookie");
                await invocationContext.Hub.Clients.Caller
                    .SendAsync(HubMethods.BetRejected, "Invalid cookie");
            
                return null;
            }
        }
        
        return await next(invocationContext);
    }
}