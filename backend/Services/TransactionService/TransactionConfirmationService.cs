namespace WalletBackend.Services.TransactionService;
public class TransactionConfirmationService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TransactionConfirmationService> _logger;
    private Timer _timer;

    public TransactionConfirmationService(IServiceProvider services, ILogger<TransactionConfirmationService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TransactionConfirmationService started.");
        _timer = new Timer(async (state) => await DoWork(state), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        await Task.CompletedTask;
    }

    private async Task DoWork(object state)
    {
        try
        {
            using (var scope = _services.CreateScope())
            {
                var transactionService = scope.ServiceProvider.GetRequiredService<ITransactionService>();
                await transactionService.UpdateTransactionConfirmationsAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TransactionConfirmationService during confirmation check.");
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TransactionConfirmationService stopping.");
        _timer?.Change(Timeout.Infinite, 0);
        await base.StopAsync(stoppingToken);
    }
}