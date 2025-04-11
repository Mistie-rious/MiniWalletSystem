using Microsoft.EntityFrameworkCore;
using Nethereum.BlockchainProcessing.BlockStorage.Entities;
using Nethereum.Web3;
using WalletBackend.Data;
using WalletBackend.Services.Functions;
using Transaction = System.Transactions.Transaction;

namespace WalletBackend.Services.TransactionService;

public class TransactionMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _nodeUrl;
    private readonly TimeSpan _updateInterval;
    private readonly ILogger<TransactionMonitorService> _logger;

    public TransactionMonitorService(IServiceScopeFactory scopeFactory,IConfiguration configuration, ILogger<TransactionMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _updateInterval = TimeSpan.FromSeconds(5);
        _nodeUrl = configuration["Ethereum:NodeUrl"];
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

                    var transactions = await context.Transactions.ToListAsync(stoppingToken);

                    var latestBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                    
                    var blockTransaction = await web3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(latestBlock);
                    
                    var walletAddresses = await context.Wallets.Select(w => w.Address.ToLower()).ToListAsync(stoppingToken);

                    foreach (var tx in blockTransaction.Transactions)
                    {
                        if (walletAddresses.Contains(tx.From.ToLower()) || walletAddresses.Contains(tx.To?.ToLower()))
                        {
                            var newTransaction = new Models.Transaction
                            {
                                TransactionHash = tx.TransactionHash,
                                SenderAddress = tx.From,
                                ReceiverAddress = tx.To,
                                Amount = Web3.Convert.FromWei(tx.Value),
                                BlockNumber = tx.BlockNumber.ToString(),
                                Currency = "Ethereum",
                                BlockchainReference = tx.BlockHash,
                                WalletId = await context.Wallets
                                    .Where(w => w.Address.ToLower() == tx.From.ToLower() ||
                                                w.Address.ToLower() == tx.To.ToLower())
                                    .Select(w => w.Id)
                                    .FirstOrDefaultAsync(stoppingToken),
                                Status = TransactionFunctions.DetermineTransactionStatus(tx, latestBlock),

                                // For enum TransactionType - determine based on tx properties
                                Type = TransactionFunctions.DetermineTransactionType(tx, walletAddresses),
                                
                                CreatedAt = DateTime.UtcNow,

                                // For description
                                Description = TransactionFunctions.GenerateTransactionDescription(tx, walletAddresses),



                            };
                            context.Transactions.Add(newTransaction);
                        }
                        await context.SaveChangesAsync(stoppingToken);
                        await Task.Delay(_updateInterval, stoppingToken);
                    }
                    
                    


                    
               

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
            }
        }
    }
}