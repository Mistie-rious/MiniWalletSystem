namespace WalletBackend.Models;

public class EtherscanOptions
{
    public string ApiKey { get; set; }
    public string BaseUrl { get; set; }
    public int RequestDelayMs { get; set; }
}
