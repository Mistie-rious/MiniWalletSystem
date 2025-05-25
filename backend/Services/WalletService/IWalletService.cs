using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WalletBackend.Models;

namespace WalletBackend.Services.WalletService;

public interface IWalletService
{
    (string EncryptedKeyStore, string Address, string Mnemonic) CreateNewWallet(string password);
    string SignTransaction(string encryptedKeyStore, string password, string transactionData);
    Task<List<Transaction>> GetTransactions(Guid walletId, string userId);


    Task<decimal> GetBalanceByUserIdAsync(string userId);
}