using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json.Serialization;
using CryptoCrush.Configuration;
using CryptoCrush.Data;
using CryptoCrush.DbInitializer;
using CryptoCrush.Filters;
using CryptoCrush.Hubs;
using CryptoCrush.Services.Hub;
using CryptoCrush.Services.Monad;
using CryptoCrush.Services.Token;
using DotNetEnv;
using DotNetEnv.Configuration;
using Mcrio.Configuration.Provider.Docker.Secrets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var myAllowSpecificOrigins = "_myAllowSpecificOrigins";

builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddDotNetEnv()
    .AddDockerSecrets();

builder.Services.Configure<EnvVariables>(options =>
{
    builder.Configuration
        .GetSection(nameof(EnvVariables))
        .Bind(options);

    foreach (var prop in typeof(EnvVariables).GetProperties())
    {
        var secretKey = EnvVariables.ToDockerVariables(prop.Name);
        var secretVal = builder.Configuration[secretKey];
        if (!string.IsNullOrWhiteSpace(secretVal))
            prop.SetValue(options, Convert.ChangeType(secretVal, prop.PropertyType));
    }
});

var envSection = builder.Configuration.GetSection(nameof(EnvVariables));

var connectionString = envSection.GetValue<string>(
    nameof(EnvVariables.PostgresConnectionString)
) ?? throw new InvalidOperationException("PostgresConnectionString is missing");

var cookieName = envSection.GetValue<string>(
    nameof(EnvVariables.CookieName)
) ?? throw new InvalidOperationException("CookieName is missing");

var jwtSecret = envSection.GetValue<string>(
    nameof(EnvVariables.JwtTokenSecret)
) ?? throw new InvalidOperationException("JwtTokenSecret is missing");

if (cookieName != "VerifyAddress")
    Console.WriteLine($"Invalid cookie name: {cookieName}");

builder.Services.AddDbContextFactory<ApiDbContext>(opts =>
    opts.UseNpgsql(connectionString));

builder.Services.AddControllers();
builder.Services.AddSignalR()
    .AddHubOptions<GameHub>(opts => opts.AddFilter<HubAuthorize>())
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<BettingService>();
builder.Services.AddSingleton<HubAuthorize>();
builder.Services.AddSingleton<CryptoPriceService>();
builder.Services.AddScoped<IUserIdentity, UserIdentity>();
builder.Services.AddSingleton<IMonadService, MonadService>();
builder.Services.AddScoped<IDbInitializer, DbInitializer>();
builder.Services.AddSingleton<HubBettingService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<CryptoPriceService>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<BettingService>());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.AddCors(options =>
{
    options.AddPolicy(name: myAllowSpecificOrigins,
        policyBuilder =>
        {
            policyBuilder.WithOrigins("https://crypto-crush-api.duckdns.org")
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
    options.RequireHttpsMetadata = true;
    options.SaveToken = true;
    options.TokenValidationParameters = new()
    {
        ValidateIssuerSigningKey = true,
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
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
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseHttpsRedirection();
SeedDatabase();

app.UseCors(myAllowSpecificOrigins);

app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.None,
    HttpOnly = HttpOnlyPolicy.Always,
    Secure = CookieSecurePolicy.Always
});
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/gamehub");


app.Run();
void SeedDatabase()
{
    using var scope = app.Services.CreateScope();
    var dbInitializer = scope.ServiceProvider.GetRequiredService<IDbInitializer>();
    dbInitializer.Initialize();
}
