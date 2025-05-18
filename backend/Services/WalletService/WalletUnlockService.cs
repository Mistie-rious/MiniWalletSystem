using Microsoft.Extensions.Caching.Memory;
using Nethereum.KeyStore;
using WalletBackend.Data;

namespace WalletBackend.Services.WalletService;

public class WalletUnlockService : IWalletUnlockService
{
    private readonly WalletContext _context;
    private readonly IMemoryCache _cache;
    private readonly IWalletService _walletService;

    public WalletUnlockService(
    WalletContext context,
    IMemoryCache cache,
        IWalletService   walletService)
    {
        _context = context;
        _cache         = cache;
        _walletService = walletService;
    }

    public async Task UnlockAsync(Guid walletId, string passphrase)
    {
        // 1) Load wallet
        var wallet = await _context.Wallets.FindAsync(walletId);
        if (wallet == null)
            throw new KeyNotFoundException("Wallet not found");

        // 2) Decrypt keystore

        byte[] pkBytes;
        try
        {
            var keyStoreService = new KeyStoreService();
            pkBytes = keyStoreService.DecryptKeyStoreFromJson(passphrase, wallet.EncryptedKeyStore);
        }
        catch
        {
            throw new UnauthorizedAccessException("Invalid passphrase");
        }

        // 3) Cache for 30 minutes
        string privateKeyHex = ByteArrayToHex(pkBytes);

        // 4) Zero out the byte array for safety
        Array.Clear(pkBytes, 0, pkBytes.Length);

        // 4) Cache the hex string for 30 minutes
        var cacheKey = GetCacheKey(walletId);
        _cache.Set(cacheKey, privateKeyHex, new MemoryCacheEntryOptions {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        });
    }


    public bool TryGetPrivateKey(Guid walletId, out string privateKey)
    {
        return _cache.TryGetValue(GetCacheKey(walletId), out privateKey);
    }

    private static string GetCacheKey(Guid walletId) 
        => $"PrivKey:{walletId}";
    
    private static string ByteArrayToHex(byte[] bytes)
    {
        // BitConverter produces uppercase hex with dashes: "AA-BB-CC", so strip dashes & lowercase
        var hex = BitConverter.ToString(bytes).Replace("-", "");
        return hex.ToLowerInvariant();
    }
}
