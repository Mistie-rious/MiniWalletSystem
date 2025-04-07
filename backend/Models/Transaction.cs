using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WalletBackend.Models.Enums;

namespace WalletBackend.Models;

public class Transaction
{
    public int Id { get; set; }
    
    [Required]
    public int WalletId { get; set; }
    
    [ForeignKey((nameof(WalletId)))]
    public required Wallet Wallet { get; set; }
    
    public string TransactionHash { get; set; }
    public string FromAddress { get; set; }
    
    public string ToAddress { get; set; }
   
    
    
    [Required]
    public required TransactionStatus Status { get; set; }
    
    [Required]
    public required TransactionType Type { get; set; }
    
    public required DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    
    [MaxLength(10)]
    public required string Currency { get; set; } = "NGN";
    
    [MaxLength(255)]
    public string? Description { get; set; }
    
    [MaxLength(255)]
    public string? BlockchainReference { get; set; }
    
    [MaxLength(255)]
    public string? BlockNumber { get; set; }


}