using System.Collections.Generic;

namespace WalletBackend.Models.Etherscan;

public class EtherscanTransactionResponse
{
    public string Status { get; set; }
    public string Message { get; set; }
    public List<EtherscanTransaction> Result { get; set; }
}