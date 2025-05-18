using System.ComponentModel.DataAnnotations;

namespace WalletBackend.Models.DTOS.Transaction;

public class TransactionRequest
{
    public string SenderAddress { get; set; }
 
    public string Description { get; set; }
    public Guid WalletId { get; set; }
    public string ReceiverAddress { get; set; }
    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }
    

 
}