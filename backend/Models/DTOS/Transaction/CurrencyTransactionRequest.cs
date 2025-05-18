using System.ComponentModel.DataAnnotations;
using WalletBackend.Models.Enums;

namespace WalletBackend.Models.DTOS.Transaction;

public class CurrencyTransactionRequest : TransactionRequest
{
    [Required]
    public CurrencyType Currency { get; set; }
    
    // Optional: Specify the network for cross-chain transactions
    public string? Network { get; set; }
}