using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Signer;
using WalletBackend.Data;
using WalletBackend.Models.Responses;
using Transaction = WalletBackend.Models.Transaction;

namespace WalletBackend.Services.WalletService;

public class WalletService : IWalletService
{
    
    private readonly WalletContext _context;

    public WalletService(WalletContext context)
    {
        _context = context;
    }
    public (string PrivateKey, string PublicKey) CreateNewWallet()
    {
        var ecKey = EthECKey.GenerateKey();
        string privateKey = ecKey.GetPrivateKey();
        string address = ecKey.GetPublicAddress();
        return (privateKey, address);
    }

    public async Task<List<Transaction>> GetTransactions(int walletId, string userId)
    {
        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.Id == walletId && w.UserId == userId);

        if (wallet == null)
        {
            return null;

        }

        return await _context.Transactions
            .Where(t => t.WalletId == walletId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }
}