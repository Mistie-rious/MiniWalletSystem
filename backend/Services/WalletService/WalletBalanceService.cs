using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nethereum.Web3;
using WalletBackend.Data;
using WalletBackend.Models;

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
        _updateInterval = TimeSpan.FromMinutes(2); // More reasonable for wallet balances
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
                    
                    _logger.LogDebug("Processing {Count} wallets for balance updates", wallets.Count);

                    var updatedWallets = new List<Wallet>(); // Use proper type instead of object
                    
                    // Process wallets one by one
                    foreach (var wallet in wallets)
                    {
                        try
                        {
                            // Create timeout for individual request
                            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);
                            
                            var balanceWei = await web3.Eth.GetBalance.SendRequestAsync(wallet.Address);
                            var balanceEth = Web3.Convert.FromWei(balanceWei);
                            
                            // Convert to decimal for comparison - use more precise conversion
                            var newBalance = (decimal)balanceEth;
                            
                            // More robust comparison - check if difference is significant
                            var balanceDifference = Math.Abs(wallet.Balance - newBalance);
                            var significantChange = balanceDifference > 0.000001m; // Increased threshold
                            
                            _logger.LogTrace("Wallet {Address}: Current={Current}, New={New}, Diff={Diff}, Update={ShouldUpdate}", 
                                wallet.Address, wallet.Balance, newBalance, balanceDifference, significantChange);
                            
                            if (significantChange)
                            {
                                _logger.LogDebug("Balance changed for {Address}: {Old} -> {New}", 
                                    wallet.Address, wallet.Balance, newBalance);
                                    
                                wallet.Balance = newBalance;
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
                        _logger.LogInformation("Updated {Count} wallet balances out of {Total} total wallets", 
                            updatedWallets.Count, wallets.Count);
                    }
                    else
                    {
                        _logger.LogDebug("No wallet balance changes detected");
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