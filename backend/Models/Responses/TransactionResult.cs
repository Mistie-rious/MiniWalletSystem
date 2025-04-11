namespace WalletBackend.Models.Responses;

public class TransactionResult
{
    public bool Success { get; set; }
    public Guid TransactionId { get; set; }
    public string Message { get; set; }
    public string BlockchainReference { get; set; }
}