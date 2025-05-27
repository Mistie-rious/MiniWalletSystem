using System;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WalletBackend.Models;
using WalletBackend.Models.Enums;

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
    
    public DbSet<WalletBalance> WalletBalances { get; set; }
    public DbSet<CurrencyConfig> CurrencyConfigs { get; set; }
    public DbSet<PendingTransactionHash> PendingTransactionHashes { get; set; }
    public DbSet<WalletScanProgress> WalletScanProgress { get; set; }



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
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.Property(t => t.Type)
                .HasConversion<string>();  // Type stored as string

            entity.Property(e => e.Status)
                .HasConversion<int>();    // Status stored as int

            entity.Property(e => e.Currency)
                .HasConversion<int>();    // Currency stored as int
        });


            
        modelBuilder.Entity<PendingTransactionHash>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TransactionHash).IsRequired().HasMaxLength(66);
            entity.Property(e => e.WalletAddress).IsRequired().HasMaxLength(42);
            entity.HasIndex(e => e.TransactionHash).IsUnique();
            entity.HasIndex(e => new { e.WalletAddress, e.IsProcessed });
        });

        // Configure WalletScanProgress
        modelBuilder.Entity<WalletScanProgress>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WalletAddress).IsRequired().HasMaxLength(42);
            entity.HasIndex(e => e.WalletAddress).IsUnique();
        });

        modelBuilder.Entity<ApplicationUser>()
            .Property(t => t.Role)
            .HasConversion<string>();
        
        modelBuilder.Entity<CurrencyConfig>().HasData(
            new CurrencyConfig {
                Id              = Guid.Parse("a1111111-2222-3333-4444-555555555555"),
                Currency        = CurrencyType.ETH,
                Name            = "Ethereum",
                Symbol          = "ETH",
                Decimals        = 18,
                Network         = "Sepolia",
                NodeUrl         = "https://eth-sepolia.g.alchemy.com/v2/OPEVNKgEfgyM3w8EfPeRVzmnglTuYeEA",
                WebSocketUrl = "wss://eth-sepolia.g.alchemy.com/v2/OPEVNKgEfgyM3w8EfPeRVzmnglTuYeEA",
                ChainId = 11155111,

                IsActive        = true,
                CreatedAt       = new DateTime(2025, 5, 1, 0, 0, 0) 
            }
            // …add other currencies here…
        );
            
        
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