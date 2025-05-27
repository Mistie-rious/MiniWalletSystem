namespace WalletBackend.Models.Etherscan;

public class EtherscanTransaction
{
    public string BlockNumber { get; set; }
    public long TimeStamp { get; set; }
    public string Hash { get; set; }
    public string BlockHash { get; set; }
    public string From { get; set; }
    public string To { get; set; }
    public string Value { get; set; }
    public string IsError { get; set; }
    public string Gas { get; set; }
    public string GasPrice { get; set; }
    public string GasUsed { get; set; }
}