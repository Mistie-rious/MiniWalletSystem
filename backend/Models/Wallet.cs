using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WalletBackend.Models;

public class Wallet
{
    public int Id { get; set; }
    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public required decimal Balance { get; set; }
    [Required]
    public string UserId { get; set; }
    [ForeignKey(nameof(UserId))]
   public ApplicationUser User { get; set; }
   
   [Required]
   public string Address { get; set; }
   
   public DateTime CreatedAt { get; set; }
   public DateTime? UpdatedAt { get; set; }
   
   public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

}