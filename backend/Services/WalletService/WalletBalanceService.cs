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

                    
                    var walletTasks = wallets.Select(async wallet => {
                        var balanceWei = await web3.Eth.GetBalance.SendRequestAsync(wallet.Address);
                        var balanceEth = (decimal)Web3.Convert.FromWei(balanceWei);
                        
                        if (Math.Abs(wallet.Balance - balanceEth) > 0.00000001m)

                        {
                            wallet.Balance = balanceEth;
                            wallet.UpdatedAt = DateTime.UtcNow;
                            return wallet;
                        }
                        
                        return null; 
                    }).ToList();
                    
                    // Wait for all tasks to complete
                    await Task.WhenAll(walletTasks);
                    
                
                    foreach (var task in walletTasks)
                    {
                        var updatedWallet = await task;
                        if (updatedWallet != null)
                        {
                            context.Update(updatedWallet);
                        }
                    }
                    
                    await context.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception e)
            {
                
               _logger.LogError(e, e.Message);
            }
            await Task.Delay(_updateInterval, stoppingToken);
        }
    }
}