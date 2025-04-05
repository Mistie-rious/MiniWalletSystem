using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WalletBackend.Models;

namespace WalletBackend.Data;

public class WalletContext: IdentityDbContext<ApplicationUser>
{
    public WalletContext(DbContextOptions<WalletContext> options)
    :base(options)
    {
        
    }
    
    public DbSet<ApplicationUser> Users { get; set; }
    public DbSet<Wallet> Wallets { get; set; }
    public DbSet<Transaction> Transactions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Wallet>()
            .HasOne(w => w.User)
            .WithOne(w => w.Wallet)
            .HasForeignKey<Wallet>(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.Entity<Transaction>()
            .Property(t => t.Status)
            .HasConversion<string>();
        
        modelBuilder.Entity<Transaction>()
            .Property(t => t.Type)
            .HasConversion<string>();

            

        modelBuilder.Entity<ApplicationUser>()
            .Property(t => t.Role)
            .HasConversion<string>();
            
        
        base.OnModelCreating(modelBuilder);

    }
    
    public override int SaveChanges()
    {
        foreach (var entry in ChangeTracker.Entries()
                     .Where(e => e.State == EntityState.Modified))
        {
            if (entry.Entity is Transaction transaction)
            {
                transaction.UpdatedAt = DateTime.UtcNow;
            }
            else if (entry.Entity is ApplicationUser user)
            {
                user.UpdatedAt = DateTime.UtcNow;
            }
        }
        return base.SaveChanges();
    }

}