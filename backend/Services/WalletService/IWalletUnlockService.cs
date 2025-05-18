namespace WalletBackend.Services.WalletService;

public interface IWalletUnlockService
{
    Task UnlockAsync(Guid walletId, string passphrase);
    bool TryGetPrivateKey(Guid walletId, out string privateKey);
}