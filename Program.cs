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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var myAllowSpecificOrigins = "_myAllowSpecificOrigins";

if (builder.Environment.IsDevelopment())
{
    Env.Load();
}
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddKeyPerFile("/run/secrets", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();
builder.Services.Configure<EnvVariables>(
    builder.Configuration.GetSection("EnvVariables"));

builder.Services.AddDbContextFactory<ApiDbContext>(opts =>
    opts.UseNpgsql(Environment.GetEnvironmentVariable("ENVVARIABLES__POSTGRES_CONNECTION_STRING")));
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
            Encoding.ASCII.GetBytes(
                Environment.GetEnvironmentVariable("ENVVARIABLES__JWT_TOKEN_SECRET")!
            )
        )
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (context.Request.Cookies.TryGetValue(Environment.GetEnvironmentVariable("CookieName")!, out var token))
            {
                context.Token = token;
            }

            if (string.IsNullOrEmpty(context.Token))
            {
                context.Token = context.Request.Query[Environment.GetEnvironmentVariable("CookieName")!];
            }
            
            return Task.CompletedTask;
        }
    };
});

var app = builder.Build();
Console.Write(Environment.GetEnvironmentVariable("ENVVARIABLES__POSTGRES_CONNECTION_STRING"));
SeedDatabase();

app.UseCors(myAllowSpecificOrigins);

app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.None,
    HttpOnly = HttpOnlyPolicy.Always,
    Secure = CookieSecurePolicy.Always
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