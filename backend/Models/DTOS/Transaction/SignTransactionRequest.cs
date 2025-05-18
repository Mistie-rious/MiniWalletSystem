using System.ComponentModel.DataAnnotations;

namespace WalletBackend.Models.DTOS.Transaction;

public class SignTransactionRequest
{
    [Required]
    public string EncryptedKeyStore { get; set; }
    
    [Required]
    public string password { get; set; }
    
    [Required]
    public string TransactionData { get; set; }
}