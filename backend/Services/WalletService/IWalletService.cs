namespace WalletBackend.Services.WalletService;

public interface IWalletService
{
    (string PrivateKey, string PublicKey) CreateNewWallet();
}