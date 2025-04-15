using System.ComponentModel.DataAnnotations;
using WalletBackend.Models.Enums;

namespace WalletBackend.Models.DTOS.Transaction;

public class ViewTransactionModel
{
    public Guid Id { get; set; }
    [Required]
    public decimal Amount { get; set; }
    [Required]
    public DateTime Date { get; set; }
    [Required]
    public TransactionStatus Status { get; set; }
    [Required]
    public TransactionType Type { get; set; }

}