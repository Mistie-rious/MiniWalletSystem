using System;
using System.ComponentModel.DataAnnotations;

namespace WalletBackend.Models;

public class WalletScanProgress
{
    [Key]
    public Guid Id { get; set; }
        
    [Required]
    [StringLength(42)] // 0x + 40 hex characters
    public string WalletAddress { get; set; }
        
    public long LastScannedBlock { get; set; }
        
    public DateTime UpdatedAt { get; set; }
}