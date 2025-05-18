using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WalletBackend.Models.Enums;

namespace WalletBackend.Models;
public class Transaction
{
    public Guid Id { get; set; }
    
    [Required]
    public Guid WalletId { get; set; }
    [ForeignKey(nameof(WalletId))]
    public Wallet Wallet { get; set; }
    
    // ADD: Currency type
    [Required]
    public CurrencyType Currency { get; set; }
    
    public string TransactionHash { get; set; }
    public string SenderAddress { get; set; }
    public string ReceiverAddress { get; set; }
    
    [Required]
    public required TransactionStatus Status { get; set; }
    
    [Required]
    public required TransactionType Type { get; set; }
    
    public required DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    
    [Required]
    [Column(TypeName = "decimal(18,8)")] // Increased precision
    public decimal Amount { get; set; }
    
    [Required]
    public DateTime? Timestamp { get; set; }
    
    [MaxLength(255)]
    public string? Description { get; set; }
    
    [MaxLength(255)]
    public string? BlockchainReference { get; set; }
    
    [MaxLength(255)]
    public string? BlockNumber { get; set; }
    
    // Optional: Network identifier for cross-chain support
    public string? Network { get; set; }
}
