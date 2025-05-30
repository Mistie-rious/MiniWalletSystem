using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nethereum.KeyStore;
using WalletBackend.Data;
using WalletBackend.Services.WalletService;


public class WalletUnlockService : IWalletUnlockService
{
    private readonly WalletContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<WalletUnlockService> _logger;

    public WalletUnlockService(WalletContext context, IMemoryCache cache, ILogger<WalletUnlockService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task UnlockAsync(Guid walletId, string password)
    {
        var wallet = await _context.Wallets.FindAsync(walletId);
        if (wallet == null)
            throw new KeyNotFoundException("Wallet not found");

        byte[] pkBytes;
        try
        {
            var keyStoreService = new KeyStoreService();
            pkBytes = keyStoreService.DecryptKeyStoreFromJson(password, wallet.EncryptedKeyStore);
        }
        catch
        {
            throw new UnauthorizedAccessException("Invalid password");
        }

        string privateKeyHex = ByteArrayToHex(pkBytes);
        Array.Clear(pkBytes, 0, pkBytes.Length);

        var cacheKey = GetCacheKey(walletId);
        _cache.Set(cacheKey, privateKeyHex, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
        });

        _logger.LogInformation("Wallet {walletId} unlocked", walletId);
    }

    public bool IsUnlocked(Guid walletId)
    {
        return _cache.TryGetValue(GetCacheKey(walletId), out _);
    }

    public void Lock(Guid walletId)
    {
        _cache.Remove(GetCacheKey(walletId));
        _logger.LogInformation("Wallet {walletId} locked", walletId);
    }

    public bool TryGetPrivateKey(Guid walletId, out string privateKey)
    {
        return _cache.TryGetValue(GetCacheKey(walletId), out privateKey);
    }

    private static string GetCacheKey(Guid walletId) => $"PrivKey:{walletId}";

    private static string ByteArrayToHex(byte[] bytes)
    {
        var hex = BitConverter.ToString(bytes).Replace("-", "");
        return hex.ToLowerInvariant();
    }
}
