using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using Nethereum.HdWallet;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.KeyStore;
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

    public (string EncryptedKeyStore, string Address, string Mnemonic) CreateNewWallet(string password)
    {
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.TwentyFour);
        string mnemonicPhrase = mnemonic.ToString();

        var wallet = new Wallet(mnemonicPhrase, null);
        var account = wallet.GetAccount(0);
        string privateKey = account.PrivateKey;
        string address = account.Address;

        var keyStoreService = new KeyStoreService();
        string encryptedKeyStore = keyStoreService.EncryptAndGenerateDefaultKeyStoreAsJson(
            password, 
            privateKey.HexToByteArray(), 
            address);
    
        return (encryptedKeyStore, address, mnemonicPhrase);
    }

    public string SignTransaction(string encryptedKeyStore, string password, string transactionData)
    {
        var keyStoreService = new KeyStoreService();
        byte[] privateKeyBytes = keyStoreService.DecryptKeyStoreFromJson(password, encryptedKeyStore);

        var privateKey = privateKeyBytes.ToHex();
        var signer = new MessageSigner();
        string signature = signer.HashAndSign(transactionData, privateKey);

        Array.Clear(privateKeyBytes, 0, privateKeyBytes.Length);

        return signature;
    }

    public async Task<List<Transaction>> GetTransactions(Guid walletId, string userId)
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