using System.IdentityModel.Tokens.Jwt;
using System.Text;
using CryptoCrush.Configuration;
using CryptoCrush.Data;
using CryptoCrush.DbInitializer;
using CryptoCrush.Filters;
using CryptoCrush.Hubs;
using CryptoCrush.Services.Hub;
using CryptoCrush.Services.Monad;
using CryptoCrush.Services.Token;
using DotNetEnv;
using Mcrio.Configuration.Provider.Docker.Secrets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var myAllowSpecificOrigins = "_myAllowSpecificOrigins";
string connectionString, cookieName, jwtSecret;

builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

if (builder.Environment.IsDevelopment())
{
    Env.Load();
    builder.Configuration.AddEnvironmentVariables();
    connectionString = Environment.GetEnvironmentVariable(EnvVariables
        .ToEnvVariables(nameof(EnvVariables.PostgresConnectionString))) ?? string.Empty;
    cookieName = Environment.GetEnvironmentVariable(nameof(EnvVariables.CookieName)) ?? string.Empty;
    jwtSecret = Environment.GetEnvironmentVariable(EnvVariables
        .ToEnvVariables(nameof(EnvVariables.JwtTokenSecret))) ?? string.Empty;
}
else
{
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddDockerSecrets();
    connectionString = 
        builder.Configuration[EnvVariables.ToDockerVariables(nameof(EnvVariables.PostgresConnectionString))] ?? string.Empty;
    cookieName = Environment.GetEnvironmentVariable(nameof(EnvVariables.CookieName)) ?? string.Empty;
    jwtSecret = 
        builder.Configuration[EnvVariables.ToDockerVariables(nameof(EnvVariables.JwtTokenSecret))] ?? string.Empty;
}

builder.Services.Configure<EnvVariables>(options =>
{
    builder.Configuration.GetSection("EnvVariables").Bind(options);
    
    if (builder.Environment.IsDevelopment())
    {
        var toEnvVars = EnvVariables.ToEnvVariables;
        
        options.RpcUrl = Environment
            .GetEnvironmentVariable(toEnvVars(nameof(EnvVariables.RpcUrl))) ?? string.Empty;
        options.ContractOwnerPrivateKey = Environment
            .GetEnvironmentVariable(toEnvVars(nameof(EnvVariables.ContractOwnerPrivateKey))) ?? string.Empty;
        options.NetworkId = Environment
            .GetEnvironmentVariable(toEnvVars(nameof(EnvVariables.NetworkId))) ?? string.Empty;
        options.ContractAddress = Environment
            .GetEnvironmentVariable(toEnvVars(nameof(EnvVariables.ContractAddress))) ?? string.Empty;
        options.JwtTokenSecret = Environment
            .GetEnvironmentVariable(toEnvVars(nameof(EnvVariables.JwtTokenSecret))) ?? string.Empty;
        options.PostgresConnectionString = Environment
            .GetEnvironmentVariable(toEnvVars(nameof(EnvVariables.PostgresConnectionString))) ?? string.Empty;
    }
    else
    {
        var toDockerVars = EnvVariables.ToDockerVariables;
        
        options.RpcUrl = 
            builder.Configuration[toDockerVars(nameof(EnvVariables.RpcUrl))] ?? string.Empty;
        options.ContractOwnerPrivateKey = 
            builder.Configuration[toDockerVars(nameof(EnvVariables.ContractOwnerPrivateKey))] ?? string.Empty;
        options.NetworkId = 
            builder.Configuration[toDockerVars(nameof(EnvVariables.NetworkId))] ?? string.Empty;
        options.ContractAddress = 
            builder.Configuration[toDockerVars(nameof(EnvVariables.ContractAddress))] ?? string.Empty;
        options.JwtTokenSecret = 
            builder.Configuration[toDockerVars(nameof(EnvVariables.JwtTokenSecret))] ?? string.Empty;
        options.PostgresConnectionString = 
            builder.Configuration[toDockerVars(nameof(EnvVariables.PostgresConnectionString))] ?? string.Empty;
    }
});

builder.Services.AddDbContextFactory<ApiDbContext>(opts =>
    opts.UseNpgsql(connectionString));

builder.Services.AddSignalR()
    .AddHubOptions<GameHub>(opts => opts.AddFilter<HubAuthorize>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<BettingService>();
builder.Services.AddSingleton<HubAuthorize>();
builder.Services.AddSingleton<CryptoPriceService>();
builder.Services.AddSingleton<IUserIdentity, UserIdentity>();
builder.Services.AddSingleton<IMonadService, MonadService>();
builder.Services.AddScoped<IDbInitializer, DbInitializer>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<CryptoPriceService>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<BettingService>());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: myAllowSpecificOrigins,
        policyBuilder =>
        {
            policyBuilder.WithOrigins("http://localhost:3000")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    //options.RequireHttpsMetadata = true;
    options.SaveToken = true;
    options.TokenValidationParameters = new()
    {
        ValidateIssuerSigningKey = true,
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = JwtRegisteredClaimNames.Sub,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.ASCII.GetBytes(jwtSecret))
	};

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (context.Request.Cookies.TryGetValue(cookieName, out var token))
            {
                context.Token = token;
            }

            if (string.IsNullOrEmpty(context.Token))
            {
                context.Token = context.Request.Query[cookieName];
            }
            
            return Task.CompletedTask;
        }
    };
});

var app = builder.Build();
SeedDatabase();

app.UseCors(myAllowSpecificOrigins);

app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.None,
    HttpOnly = HttpOnlyPolicy.Always,
    //Secure = CookieSecurePolicy.Always
    Secure = CookieSecurePolicy.None
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/gamehub");
app.UseHttpsRedirection();

app.Run();
void SeedDatabase()
{
    using var scope = app.Services.CreateScope();
    var dbInitializer = scope.ServiceProvider.GetRequiredService<IDbInitializer>();
    dbInitializer.Initialize();
}
