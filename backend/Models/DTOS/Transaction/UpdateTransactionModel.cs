using System.ComponentModel.DataAnnotations;
using WalletBackend.Models.Enums;

namespace WalletBackend.Models.DTOS.Transaction;

public class UpdateTransactionModel
{
    
    public int Id { get; set; }
    [Required]
    public decimal Amount { get; set; }
    [Required]
    public DateTime Date { get; set; }
    [Required]
    public TransactionStatus Status { get; set; }
    [Required]
    public TransactionType Type { get; set; }
    [Required]
    [MaxLength(10)]
    public required string Currency { get; set; } = "NGN";
}