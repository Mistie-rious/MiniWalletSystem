using System;
using System.Threading.Tasks;

namespace WalletBackend.Services.WalletService;

public interface IWalletUnlockService
{
  
    bool TryGetPrivateKey(Guid walletId, out string privateKey);
    Task UnlockAsync(Guid walletId, string mnemonic);
    bool IsUnlocked(Guid walletId);
}