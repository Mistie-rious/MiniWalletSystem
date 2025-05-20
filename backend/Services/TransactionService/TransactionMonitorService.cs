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
using System.Collections.Generic;
using Nethereum.Hex.HexTypes;
using WalletBackend.Models.Enums;

namespace WalletBackend.Services.TransactionService;

public class TransactionMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _nodeUrl;
    private readonly TimeSpan _updateInterval;
    private readonly ILogger<TransactionMonitorService> _logger;
    private ulong _lastSyncedBlock = 0;
    private readonly int _blocksToScan;
    private readonly int _maxBlocksPerBatch;

    public TransactionMonitorService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<TransactionMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _updateInterval = TimeSpan.FromSeconds(configuration.GetValue<int>("Blockchain:ScanIntervalSeconds", 10));
        _nodeUrl = configuration["Ethereum:NodeUrl"] ?? throw new InvalidOperationException("Ethereum:NodeUrl configuration is required");
        
        _blocksToScan = configuration.GetValue<int>("Blockchain:BlocksToScan", 1000);
        _maxBlocksPerBatch = configuration.GetValue<int>("Blockchain:MaxBlocksPerBatch", 50);
        
        _logger.LogInformation("TransactionMonitorService initialized. Will scan {BlockCount} historical blocks with {MaxBatch} blocks per batch.", 
            _blocksToScan, _maxBlocksPerBatch);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TransactionMonitorService started");

        // Initialize the starting block point
        await InitializeBlockScanPointAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Beginning transaction scan cycle");
                
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
                    var web3 = new Web3(_nodeUrl);

                    // Get current latest block
                    var latestBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                    _logger.LogDebug("Current latest block: {CurrentBlock}, Last synced: {LastSynced}", 
                        latestBlock.Value, _lastSyncedBlock);

                    // Check if we're already up to date
                    if (_lastSyncedBlock >= (ulong)latestBlock.Value)
                    {
                        _logger.LogDebug("Already up to date with latest block");
                        await Task.Delay(_updateInterval, stoppingToken);
                        continue;
                    }

                    // Calculate end block (don't process too many blocks at once)
                    var endBlock = Math.Min(_lastSyncedBlock + (ulong)_maxBlocksPerBatch, (ulong)latestBlock.Value);
                    
                    // Get all wallet addresses
                    var walletAddresses = await context.Wallets
                        .Where(w => !string.IsNullOrEmpty(w.Address))
                        .Select(w => new { Id = w.Id, Address = w.Address.ToLower() })
                        .ToListAsync(stoppingToken);
                    
                    if (walletAddresses.Count == 0)
                    {
                        _logger.LogWarning("No wallet addresses found to monitor");
                        _lastSyncedBlock = (ulong)latestBlock.Value; // Skip ahead
                        await Task.Delay(_updateInterval, stoppingToken);
                        continue;
                    }

                    var walletAddressDict = walletAddresses.ToDictionary(w => w.Address, w => w.Id);
                    var addressList = walletAddresses.Select(w => w.Address).ToList();

                    _logger.LogInformation("Scanning blocks {StartBlock} to {EndBlock} for {WalletCount} wallets", 
                        _lastSyncedBlock + 1, endBlock, walletAddresses.Count);

                    int newTransactionsFound = 0;
                    
                    // Process blocks
                    for (var blockNumber = _lastSyncedBlock + 1; blockNumber <= endBlock; blockNumber++)
                    {
                        try
                        {
                            var blockWithTxs = await web3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(
                                new HexBigInteger(blockNumber));
                            
                            if (blockWithTxs?.Transactions == null || !blockWithTxs.Transactions.Any())
                            {
                                _logger.LogTrace("Block {BlockNumber} has no transactions", blockNumber);
                                continue;
                            }

                            _logger.LogTrace("Processing {Count} transactions in block {BlockNumber}", 
                                blockWithTxs.Transactions.Length, blockNumber);
                            
                            // Get transaction receipts for status determination
                            var relevantTransactions = new List<(Nethereum.RPC.Eth.DTOs.Transaction tx, Guid walletId, bool isIncoming)>();
                            
                            // Check each transaction for relevance to our wallets
                            foreach (var tx in blockWithTxs.Transactions)
                            {
                                var fromAddress = tx.From?.ToLower();
                                var toAddress = tx.To?.ToLower();
                                
                                // Check if this transaction involves any of our wallets
                                if (!string.IsNullOrEmpty(fromAddress) && walletAddressDict.ContainsKey(fromAddress))
                                {
                                    // Outgoing transaction
                                    relevantTransactions.Add((tx, walletAddressDict[fromAddress], false));
                                }
                                
                                if (!string.IsNullOrEmpty(toAddress) && walletAddressDict.ContainsKey(toAddress))
                                {
                                    // Incoming transaction
                                    relevantTransactions.Add((tx, walletAddressDict[toAddress], true));
                                }
                            }
                            
                            // Process relevant transactions
                            foreach (var (tx, walletId, isIncoming) in relevantTransactions)
                            {
                                // Check if transaction already exists
                                var exists = await context.Transactions
                                    .AnyAsync(t => t.TransactionHash == tx.TransactionHash && t.WalletId == walletId, stoppingToken);
                                
                                if (!exists)
                                {
                                    _logger.LogInformation("Found new {Direction} transaction {Hash} for wallet {WalletId} in block {Block}", 
                                        isIncoming ? "incoming" : "outgoing", tx.TransactionHash, walletId, blockNumber);
                                    
                                    // Get transaction receipt for status
                                    var receipt = await web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(tx.TransactionHash);
                                    
                                    // Determine transaction status
                                    TransactionStatus status;
                                    if (receipt == null)
                                    {
                                        status = TransactionStatus.Pending;
                                    }
                                    else
                                    {
                                        status = receipt.Status.Value == 1 ? TransactionStatus.Successful : TransactionStatus.Failed;
                                    }
                                    
                                    // Determine transaction type
                                    TransactionType transactionType;
                                    if (isIncoming)
                                    {
                                        // Check if it's from another wallet we manage (internal transfer)
                                        var fromAddress = tx.From?.ToLower();
                                        transactionType = !string.IsNullOrEmpty(fromAddress) && walletAddressDict.ContainsKey(fromAddress) 
                                            ? TransactionType.Internal 
                                            : TransactionType.Credit;
                                    }
                                    else
                                    {
                                        // Check if it's to another wallet we manage (internal transfer)
                                        var toAddress = tx.To?.ToLower();
                                        transactionType = !string.IsNullOrEmpty(toAddress) && walletAddressDict.ContainsKey(toAddress) 
                                            ? TransactionType.Internal 
                                            : TransactionType.Debit;
                                    }
                                    
                                    // Create new transaction record
                                    var newTransaction = new Models.Transaction
                                    {
                                        TransactionHash = tx.TransactionHash,
                                        SenderAddress = tx.From ?? string.Empty,
                                        ReceiverAddress = tx.To ?? string.Empty,
                                        Amount = Web3.Convert.FromWei(tx.Value),
                                        BlockNumber = tx.BlockNumber?.ToString() ?? blockNumber.ToString(),
                                        BlockchainReference = tx.BlockHash ?? string.Empty,
                                        WalletId = walletId,
                                        Status = status,
                                        Type = transactionType,
                                        Currency = CurrencyType.ETH, // Assuming ETH for now
                                        CreatedAt = DateTime.UtcNow,
                                        UpdatedAt = DateTime.UtcNow,
                                        Description = GenerateTransactionDescription(tx, isIncoming, transactionType)
                                    };
                                    
                                    context.Transactions.Add(newTransaction);
                                    newTransactionsFound++;
                                }
                            }
                            
                            // Save changes for this block
                            if (newTransactionsFound > 0)
                            {
                                await context.SaveChangesAsync(stoppingToken);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing block {BlockNumber}", blockNumber);
                            // Continue to next block despite error
                            continue;
                        }
                    }
                    
                    // Update last synced block
                    _lastSyncedBlock = endBlock;
                    
                    if (newTransactionsFound > 0)
                    {
                        _logger.LogInformation("Scan complete. Found {Count} new transactions. Now synced to block {BlockNumber}", 
                            newTransactionsFound, _lastSyncedBlock);
                    }
                    else
                    {
                        _logger.LogDebug("Scan complete. No new transactions found. Now synced to block {BlockNumber}", _lastSyncedBlock);
                    }
                }
                
                // Wait before next scan
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
            
            // Get current block
            var currentBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            
            // Try to find the last processed transaction
            var lastTx = await context.Transactions
                .Where(t => !string.IsNullOrEmpty(t.BlockNumber))
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(stoppingToken);
                
            if (lastTx != null && ulong.TryParse(lastTx.BlockNumber, out var lastBlockNumber))
            {
                // Start from the last transaction block we have
                _lastSyncedBlock = lastBlockNumber;
                _logger.LogInformation("Found existing transactions. Starting from block {BlockNumber}", _lastSyncedBlock);
            }
            else
            {
                // No existing transactions, start scanning from configured number of blocks ago
                _lastSyncedBlock = (ulong)Math.Max(0, (long)currentBlock.Value - _blocksToScan);
                _logger.LogInformation("No existing transactions. Starting historical scan from block {BlockNumber} ({BlocksToScan} blocks ago)", 
                    _lastSyncedBlock, _blocksToScan);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing block scan point");
            
            // Default to scanning from recent blocks if there's an error
            try
            {
                var web3 = new Web3(_nodeUrl);
                var currentBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                _lastSyncedBlock = (ulong)Math.Max(0, (long)currentBlock.Value - 100);
                
                _logger.LogInformation("Defaulting to start scan from block {BlockNumber}", _lastSyncedBlock);
            }
            catch (Exception initEx)
            {
                _logger.LogError(initEx, "Failed to initialize block scan point. Starting from block 0.");
                _lastSyncedBlock = 0;
            }
        }
    }
    
    private static string GenerateTransactionDescription(Nethereum.RPC.Eth.DTOs.Transaction tx, bool isIncoming, TransactionType type)
    {
        return type switch
        {
            TransactionType.Credit => $"Received from {tx.From}",
            TransactionType.Debit => $"Sent to {tx.To}",
            TransactionType.Internal => $"Internal transfer: {tx.From} → {tx.To}",
            _ => isIncoming ? $"Incoming transaction from {tx.From}" : $"Outgoing transaction to {tx.To}"
        };
    }
}
