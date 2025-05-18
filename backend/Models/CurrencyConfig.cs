using System.ComponentModel.DataAnnotations;
using WalletBackend.Models.Enums;

namespace WalletBackend.Models;

public class CurrencyConfig
{
    public Guid Id { get; set; }
    
    [Required]
    public CurrencyType Currency { get; set; }
    
    [Required]
    public string Name { get; set; }
    
    [Required]
    public string Symbol { get; set; }
    
    [Required]
    public int Decimals { get; set; }
    
    // Network information
    public string Network { get; set; }
    public string NodeUrl { get; set; }
    public string? WebSocketUrl { get; set; }
    public int? ChainId { get; set; }
    
    // Contract address for tokens
    public string? ContractAddress { get; set; }
    
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}