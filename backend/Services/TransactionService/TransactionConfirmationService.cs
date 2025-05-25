using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WalletBackend.Services.TransactionService;

public class TransactionConfirmationService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TransactionConfirmationService> _logger;
    private readonly TimeSpan _checkInterval;

    public TransactionConfirmationService(IServiceProvider services, ILogger<TransactionConfirmationService> logger)
    {
        _services = services;
        _logger = logger;
        // Much faster for transaction confirmations
        _checkInterval = TimeSpan.FromSeconds(15); // or even 10 seconds
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var transactionService = scope.ServiceProvider
                    .GetRequiredService<ITransactionService>();
                
                var updated = await transactionService.UpdateTransactionConfirmationsBatchAsync();
                
                if (updated > 0)
                {
                    _logger.LogInformation($"Updated {updated} transaction confirmations");
                }
                
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating transaction confirmations");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task DoWork(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var transactionService = scope.ServiceProvider.GetRequiredService<ITransactionService>();
        
        var updatedCount = await transactionService.UpdateTransactionConfirmationsAsync();
        
        if (updatedCount > 0)
        {
            _logger.LogInformation("Updated {Count} transaction confirmations", updatedCount);
        }
        else
        {
            _logger.LogDebug("No pending transactions to update");
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TransactionConfirmationService stopping");
        await base.StopAsync(stoppingToken);
    }
}