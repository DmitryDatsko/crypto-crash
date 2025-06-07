namespace CryptoCrush.Configuration;
public class EnvVariables
{
    public string RpcUrl { get; set; } = string.Empty;
    public string ContractOwnerPrivateKey { get; set; } = string.Empty;
    public int NetworkId { get; set; }
    public string ContractAddress { get; set; } = string.Empty;
    public string JwtTokenSecret { get; set; } = string.Empty;
    public string PostgresConnectionString { get; set; } = string.Empty;
    public string CookieName { get; set; } = string.Empty;
    public double MempoolTimeSec { get; set; }
    public int RequiredConfirmations { get; set; }
    public decimal TotalPixels { get; set; }
    public decimal Margin { get; set; }
    public double GameDuration { get; set; }
    public double BetTime { get; set; }
    public double DelayAfterGame { get; set; }

    public static string ToEnvVariables(string pascalName)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pascalName.Length; i++)
        {
            char c = pascalName[i];
            if (i > 0 && char.IsUpper(c))
            {
                sb.Append('_');
            }
            sb.Append(char.ToUpperInvariant(c));
        }
        
        return sb.ToString();
    }
    
    public static string ToDockerVariables(string pascalName)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pascalName.Length; i++)
        {
            char c = pascalName[i];
            if (i > 0 && char.IsUpper(c))
            {
                sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        
        return sb.ToString();
    }
}