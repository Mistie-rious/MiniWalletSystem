using Microsoft.EntityFrameworkCore;
using Nethereum.Web3;
using WalletBackend.Data;
using WalletBackend.Models;
using WalletBackend.Services.Functions;
using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace WalletBackend.Services.TransactionService;

public class TransactionMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _nodeUrl;
    private readonly TimeSpan _updateInterval;
    private readonly ILogger<TransactionMonitorService> _logger;
    private ulong _lastSyncedBlock = 0;
    private readonly int _blocksToScan;

    public TransactionMonitorService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<TransactionMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _updateInterval = TimeSpan.FromSeconds(10);
        _nodeUrl = configuration["Ethereum:NodeUrl"];
        

        _blocksToScan = configuration.GetValue<int>("Blockchain:BlocksToScan", 1000);
        
        _logger.LogInformation("TransactionMonitorService initialized. Will scan {BlockCount} historical blocks.", _blocksToScan);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TransactionMonitorService started");

       
        await InitializeBlockScanPointAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Beginning transaction scan cycle");
                
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
                    var web3 = new Web3(_nodeUrl);

                  
                    var latestBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                    _logger.LogInformation("Current latest block: {CurrentBlock}, Last synced: {LastSynced}", 
                        latestBlock.Value, _lastSyncedBlock);

                   
                    if (_lastSyncedBlock >= (ulong)latestBlock.Value)
                    {
                        _logger.LogInformation("Already up to date with latest block");
                        await Task.Delay(_updateInterval, stoppingToken);
                        continue;
                    }

                 
                    var endBlock = Math.Min(_lastSyncedBlock + 50, (ulong)latestBlock.Value);
                    
                    // Get all wallet addresses
                    var walletAddresses = await context.Wallets
                        .Select(w => w.Address.ToLower())
                        .ToListAsync(stoppingToken);
                    
                    if (walletAddresses.Count == 0)
                    {
                        _logger.LogWarning("No wallet addresses found to monitor");
                        _lastSyncedBlock = (ulong)latestBlock.Value; // Skip ahead
                        await Task.Delay(_updateInterval, stoppingToken);
                        continue;
                    }

                    _logger.LogInformation("Scanning blocks {StartBlock} to {EndBlock} for {WalletCount} wallets", 
                        _lastSyncedBlock + 1, endBlock, walletAddresses.Count);

                    int newTransactionsFound = 0;
                    
                    // Process blocks
                    for (var blockNumber = _lastSyncedBlock + 1; blockNumber <= endBlock; blockNumber++)
                    {
                        try
                        {
                            var blockWithTxs = await web3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(
                                new Nethereum.Hex.HexTypes.HexBigInteger(blockNumber));
                            
                            if (blockWithTxs == null || blockWithTxs.Transactions == null || !blockWithTxs.Transactions.Any())
                            {
                                _logger.LogDebug("Block {BlockNumber} has no transactions", blockNumber);
                                continue;
                            }

                            _logger.LogDebug("Processing {Count} transactions in block {BlockNumber}", 
                                blockWithTxs.Transactions.Length, blockNumber);
                            
                            // Check each transaction
                            foreach (var tx in blockWithTxs.Transactions)
                            {
                                if (walletAddresses.Contains(tx.From.ToLower()) || 
                                   (tx.To != null && walletAddresses.Contains(tx.To.ToLower())))
                                {
                                   
                                    var exists = await context.Transactions
                                        .AnyAsync(t => t.TransactionHash == tx.TransactionHash, stoppingToken);
                                    
                                    if (!exists)
                                    {
                                        _logger.LogInformation("Found new transaction {Hash} in block {Block}", 
                                            tx.TransactionHash, blockNumber);
                                        
                                       
                                        var wallet = await context.Wallets
                                            .FirstOrDefaultAsync(w => 
                                                w.Address.ToLower() == tx.From.ToLower() || 
                                                (tx.To != null && w.Address.ToLower() == tx.To.ToLower()), 
                                                stoppingToken);
                                        
                                        if (wallet != null)
                                        {
                                            var newTransaction = new Models.Transaction
                                            {
                                                TransactionHash = tx.TransactionHash,
                                                SenderAddress = tx.From,
                                                ReceiverAddress = tx.To,
                                                Amount = Web3.Convert.FromWei(tx.Value),
                                                BlockNumber = tx.BlockNumber.ToString(),
                                                
                                                BlockchainReference = tx.BlockHash,
                                                WalletId = wallet.Id,
                                                Status = TransactionFunctions.DetermineTransactionStatus(tx, latestBlock),
                                                Type = TransactionFunctions.DetermineTransactionType(tx, walletAddresses),
                                                CreatedAt = DateTime.UtcNow,
                                                Description = TransactionFunctions.GenerateTransactionDescription(tx, walletAddresses)
                                            };
                                            
                                            context.Transactions.Add(newTransaction);
                                            await context.SaveChangesAsync(stoppingToken);
                                            newTransactionsFound++;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing block {BlockNumber}", blockNumber);
                            // Continue to next block despite error
                        }
                    }
                    
                   
                    _lastSyncedBlock = endBlock;
                    _logger.LogInformation("Scan complete. Found {Count} new transactions. Now synced to block {BlockNumber}", 
                        newTransactionsFound, _lastSyncedBlock);
                }
                
             
                await Task.Delay(_updateInterval, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in transaction scanning service");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Longer delay on error
            }
        }
    }
    
    private async Task InitializeBlockScanPointAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
            var web3 = new Web3(_nodeUrl);
            
           
            var currentBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            
      
            var lastTx = await context.Transactions
                .OrderByDescending(t => t.BlockNumber)
                .FirstOrDefaultAsync(stoppingToken);
                
            if (lastTx != null)
            {
                // Start from the last transaction block we have
                _lastSyncedBlock = ulong.Parse(lastTx.BlockNumber);
                _logger.LogInformation("Found existing transactions. Starting from block {BlockNumber}", _lastSyncedBlock);
            }
            else
            {
                // No existing transactions, start scanning from a number of blocks ago
                _lastSyncedBlock = (ulong)BigInteger.Max((BigInteger)0, currentBlock.Value - _blocksToScan);

                _logger.LogInformation("No existing transactions. Starting historical scan from block {BlockNumber}", _lastSyncedBlock);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing block scan point");
            
            // Default to scanning from recent blocks if there's an error
            var web3 = new Web3(_nodeUrl);
            var currentBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            _lastSyncedBlock = (ulong)BigInteger.Max((BigInteger)0, currentBlock.Value - 100);

       
            
            _logger.LogInformation("Defaulting to start scan from block {BlockNumber}", _lastSyncedBlock);
        }
    }
}