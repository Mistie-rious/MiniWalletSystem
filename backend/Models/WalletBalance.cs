using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WalletBackend.Models.Enums;

namespace WalletBackend.Models;

public class WalletBalance
{
    public Guid Id { get; set; }
    
    [Required]
    public Guid WalletId { get; set; }
    [ForeignKey(nameof(WalletId))]
    public Wallet Wallet { get; set; }
    
    [Required]
    public CurrencyType Currency { get; set; }
    
    [Required]
    [Column(TypeName = "decimal(18,8)")]
    public decimal Balance { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    

}