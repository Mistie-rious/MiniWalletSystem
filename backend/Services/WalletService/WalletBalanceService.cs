using Microsoft.EntityFrameworkCore;
using Nethereum.Web3;
using WalletBackend.Data;

namespace WalletBackend.Services.WalletService;

public class WalletBalanceService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _nodeUrl;
    private readonly TimeSpan _updateInterval;
    private readonly ILogger<WalletBalanceService> _logger;

    public WalletBalanceService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<WalletBalanceService> logger)
    {
        _scopeFactory = scopeFactory;
        _nodeUrl = configuration["Ethereum:NodeUrl"];
        _updateInterval = TimeSpan.FromSeconds(30);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
                    var web3 = new Web3(_nodeUrl);
                    
                    var wallets = await context.Wallets.ToListAsync(stoppingToken);
                    var walletLookup = wallets.ToDictionary(w => w.Address.ToLower(), w => w);

                    _logger.LogDebug("Loaded {Count} wallets from DB:", walletLookup.Count);
                    foreach (var addr in walletLookup.Keys)
                        _logger.LogDebug(" – DB addr: '{Address}' (len={Len})", addr, addr.Length);

                    var updatedWallets = new List<object>();
                    
                    // Process wallets one by one instead of all at once to avoid overwhelming the RPC
                    foreach (var wallet in wallets)
                    {
                        try
                        {
                            // Create timeout for individual request
                            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);
                            
                            var balanceWei = await web3.Eth.GetBalance.SendRequestAsync(wallet.Address);
                            var balanceEth = (decimal)Web3.Convert.FromWei(balanceWei);
                            
                            if (Math.Abs(wallet.Balance - balanceEth) > 0.00000001m)
                            {
                                wallet.Balance = balanceEth;
                                wallet.UpdatedAt = DateTime.UtcNow;
                                updatedWallets.Add(wallet);
                            }
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            // Service is stopping, break out
                            break;
                        }
                        catch (Exception ex)
                        {
                            // Log individual wallet error and continue with next wallet
                            _logger.LogWarning(ex, "Failed to update balance for wallet {Address}", wallet.Address);
                        }
                    }
                    
                    // Update all changed wallets at once
                    if (updatedWallets.Count > 0)
                    {
                        context.UpdateRange(updatedWallets);
                        await context.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation("Updated {Count} wallet balances", updatedWallets.Count);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error in wallet balance update cycle: {Message}", e.Message);
            }
            
            await Task.Delay(_updateInterval, stoppingToken);
        }
    }
}