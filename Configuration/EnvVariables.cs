namespace CryptoCrush.Configuration;
public class EnvVariables
{
    [ConfigurationKeyName("RPC_URL")]
    public string RpcUrl { get; init; } = string.Empty;
    [ConfigurationKeyName("CONTRACT_OWNER_PRIVATE_KEY")]
    public string ContractOwnerPrivateKey { get; init; } = string.Empty;
    [ConfigurationKeyName("NETWORK_ID")]
    public string NetworkId { get; init; } = string.Empty;
    [ConfigurationKeyName("CONTRACT_ADDRESS")]
    public string ContractAddress { get; init; } = string.Empty;
    [ConfigurationKeyName("JWT_TOKEN_SECRET")]
    public string JwtTokenSecret { get; init; } = string.Empty;
    [ConfigurationKeyName("POSTGRES_CONNECTION_STRING")]
    public string PostgresConnectionString { get; init; } = string.Empty;
    public string CookieName { get; init; } = string.Empty;
    public double MempoolTimeSec { get; init; }
    public int RequiredConfirmations { get; init; }
    public decimal TotalPixels { get; init; }
    public decimal Margin { get; init; }
    public double GameDuration { get; init; }
    public double BetTime { get; init; }
    public double DelayAfterGame { get; init; }
}