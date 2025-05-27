namespace WalletBackend.Models.Etherscan;

public class EtherscanTransactionDetailResponse
{
    public string Status { get; set; }
    public string Message { get; set; }
    public EtherscanTransaction Result { get; set; }
}