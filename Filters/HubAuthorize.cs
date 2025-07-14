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
        logger.LogInformation("In filter");
        var httpContext = invocationContext.Context.GetHttpContext() ?? throw new HubException("In filter");

        bool isAuthenticated = httpContext.User.Identity?.IsAuthenticated ?? false;
        
        if (!isAuthenticated)
        {
            throw new HubException("User is not authenticated");
        }
        
        return await next(invocationContext);
    }
}