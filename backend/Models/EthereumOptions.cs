namespace WalletBackend.Models;

public class EthereumOptions
{
    public string NodeUrl { get; set; }
    public string WebSocketUrl { get; set; }
    public int MaxConcurrentRequests { get; set; }
    public int RequestTimeoutMs { get; set; }
}
