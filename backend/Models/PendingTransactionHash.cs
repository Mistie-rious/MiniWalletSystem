using System;
using System.ComponentModel.DataAnnotations;

namespace WalletBackend.Models;

public class PendingTransactionHash
{
    [Key]
    public Guid Id { get; set; }
        
    [Required]
    [StringLength(66)] // 0x + 64 hex characters
    public string TransactionHash { get; set; }
        
    [Required]
    [StringLength(42)] // 0x + 40 hex characters
    public string WalletAddress { get; set; }
        
    public DateTime DiscoveredAt { get; set; }
        
    public bool IsProcessed { get; set; }
        
    public DateTime? ProcessedAt { get; set; }
}
