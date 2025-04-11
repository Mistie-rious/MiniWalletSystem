using WalletBackend.Models;

namespace WalletBackend.Services.WalletService;

public interface IWalletService
{
    (string EncryptedKeyStore, string Address, string Mnemonic) CreateNewWallet(string passphrase);
    string SignTransaction(string encryptedKeyStore, string passphrase, string transactionData);
    Task<List<Transaction>> GetTransactions(Guid walletId, string userId);
}