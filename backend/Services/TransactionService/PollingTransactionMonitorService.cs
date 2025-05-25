using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using WalletBackend.Data;
using WalletBackend.Models;
using WalletBackend.Models.Enums;
using WalletBackend.Services.Functions;
using Microsoft.EntityFrameworkCore;
using Transaction = WalletBackend.Models.Transaction;

namespace WalletBackend.Services.TransactionService
{
    public class PollingTransactionMonitorService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly string _httpNodeUrl;
        private readonly ILogger<PollingTransactionMonitorService> _logger;
        private readonly Web3 _httpWeb3;
        private ulong _lastSyncedBlock;
        private readonly ulong _maxCatchupBlocks = 200;
        private readonly int _rpcTimeoutSeconds;
        private readonly int _maxRetries;
        private readonly int _retryDelaySeconds;
        private readonly int _batchSize;
        private readonly int _pollingIntervalSeconds;
        private readonly int _healthCheckIntervalMinutes;
        private DateTime _lastHealthCheck = DateTime.UtcNow;

        public PollingTransactionMonitorService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<PollingTransactionMonitorService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            _httpNodeUrl = configuration["Ethereum:NodeUrl"]
                ?? throw new ArgumentException("Ethereum:NodeUrl must be set");

            _rpcTimeoutSeconds = configuration.GetValue<int>("Blockchain:RpcTimeoutSeconds", 30);
            _maxRetries = configuration.GetValue<int>("Blockchain:MaxRetries", 5);
            _retryDelaySeconds = configuration.GetValue<int>("Blockchain:RetryDelaySeconds", 3);
            _batchSize = configuration.GetValue<int>("Blockchain:BatchSize", 10);
            _pollingIntervalSeconds = configuration.GetValue<int>("Blockchain:PollingIntervalSeconds", 12); // ~Ethereum block time
            _healthCheckIntervalMinutes = configuration.GetValue<int>("Blockchain:HealthCheckIntervalMinutes", 5);
            
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer = 5
            };
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(_rpcTimeoutSeconds)
            };
            _httpWeb3 = new Web3(new RpcClient(new Uri(_httpNodeUrl), httpClient));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting PollingTransactionMonitorService with {Interval}s polling interval...", _pollingIntervalSeconds);

            try
            {
                // Step 1: Initialize the starting point (limited to max 200 blocks)
                await InitializeBlockScanPointAsync(stoppingToken);
                
                // Step 2: Quick limited catchup (max 200 blocks)
                await PerformLimitedCatchupAsync(stoppingToken);
                
                // Step 3: Start polling for new blocks
                await StartPollingAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("PollingTransactionMonitorService stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error in PollingTransactionMonitorService");
                throw;
            }
        }

        private async Task StartPollingAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting polling loop from block {Block}...", _lastSyncedBlock);
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Periodic health check
                    if (DateTime.UtcNow - _lastHealthCheck > TimeSpan.FromMinutes(_healthCheckIntervalMinutes))
                    {
                        await PerformHealthCheckAsync(cancellationToken);
                        _lastHealthCheck = DateTime.UtcNow;
                    }

                    // Check for new blocks
                    await CheckForNewBlocksAsync(cancellationToken);
                    
                    // Wait before next poll
                    await Task.Delay(TimeSpan.FromSeconds(_pollingIntervalSeconds), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in polling loop, continuing...");
                    
                    // Back off on errors to avoid hammering the node
                    await Task.Delay(TimeSpan.FromSeconds(_retryDelaySeconds * 2), cancellationToken);
                }
            }
        }

        private async Task CheckForNewBlocksAsync(CancellationToken cancellationToken)
        {
            var currentHead = await GetCurrentBlockNumberWithRetryAsync(cancellationToken);
            if (currentHead == null)
            {
                _logger.LogWarning("Failed to get current block number");
                return;
            }

            var headBlockNumber = (ulong)currentHead.Value;
            
            if (headBlockNumber <= _lastSyncedBlock)
            {
                _logger.LogDebug("No new blocks. Current: {Current}, Last synced: {Last}", headBlockNumber, _lastSyncedBlock);
                return;
            }

            var startBlock = _lastSyncedBlock + 1;
            var endBlock = headBlockNumber;
            var blocksToProcess = endBlock - startBlock + 1;

            // Limit how many blocks we process in one go to avoid overwhelming the system
            var maxBlocksPerPoll = (ulong)Math.Max(_batchSize * 2, 20);
            if (blocksToProcess > maxBlocksPerPoll)
            {
                endBlock = startBlock + maxBlocksPerPoll - 1;
                blocksToProcess = maxBlocksPerPoll;
                _logger.LogInformation("Limiting poll to {Max} blocks. Processing {Start} to {End}", maxBlocksPerPoll, startBlock, endBlock);
            }

            _logger.LogInformation("Processing {Count} new blocks from {Start} to {End}", blocksToProcess, startBlock, endBlock);

            // Process blocks in batches
            for (var batchStart = startBlock; batchStart <= endBlock; batchStart += (ulong)_batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var batchEnd = Math.Min(batchStart + (ulong)_batchSize - 1, endBlock);
                
                _logger.LogDebug("Processing batch: blocks {BatchStart} to {BatchEnd}", batchStart, batchEnd);
                
                var batchTasks = new List<Task>();
                for (var block = batchStart; block <= batchEnd; block++)
                {
                    batchTasks.Add(ProcessBlockWithExtraRetryAsync(block, cancellationToken));
                }
                
                await Task.WhenAll(batchTasks);
                
                // Update _lastSyncedBlock to the highest processed block in this batch
                _lastSyncedBlock = batchEnd;
                
                // Small delay between batches to be gentle on the RPC
                if (batchEnd < endBlock)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }

            _logger.LogInformation("Completed processing up to block {Block}", _lastSyncedBlock);
        }

        private async Task<HexBigInteger?> GetCurrentBlockNumberWithRetryAsync(CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    return await _httpWeb3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                }
                catch (Exception ex) when (attempt < _maxRetries)
                {
                    _logger.LogWarning(ex, "Failed to get block number, attempt {Attempt}/{Max}", attempt, _maxRetries);
                    await Task.Delay(TimeSpan.FromSeconds(_retryDelaySeconds * attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get block number after {Max} attempts", _maxRetries);
                    return null;
                }
            }
            return null;
        }

        private async Task PerformHealthCheckAsync(CancellationToken cancellationToken)
        {
            try
            {
                var chainId = await _httpWeb3.Eth.ChainId.SendRequestAsync();
                var blockNumber = await _httpWeb3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                
                _logger.LogDebug("Health check successful. Chain ID: {ChainId}, Block: {Block}", 
                    chainId.Value, blockNumber.Value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Health check failed, connection may be unstable");
                
                // Optionally recreate the Web3 instance on health check failure
                // This could help with connection issues but may not be necessary
            }
        }

        private async Task InitializeBlockScanPointAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WalletContext>();

            var currentHead = await GetCurrentBlockNumberWithRetryAsync(cancellationToken);
            if (currentHead == null)
            {
                throw new InvalidOperationException("Cannot get current block number for initialization");
            }
            
            var headBlockNumber = (ulong)currentHead.Value;

            var lastTx = await context.Transactions
                .OrderByDescending(t => t.BlockNumber)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastTx != null && ulong.TryParse(lastTx.BlockNumber, out var lastDbBlock))
            {
                // Ensure we don't go back more than 200 blocks from current head
                var minAllowedBlock = headBlockNumber > _maxCatchupBlocks ? headBlockNumber - _maxCatchupBlocks : 0;
                _lastSyncedBlock = Math.Max(lastDbBlock, minAllowedBlock);
                
                _logger.LogInformation("Last DB block: {LastDb}, Current head: {Head}, Starting from: {Start}", 
                    lastDbBlock, headBlockNumber, _lastSyncedBlock);
            }
            else
            {
                // Start from max 200 blocks ago if no transactions exist
                _lastSyncedBlock = headBlockNumber > _maxCatchupBlocks ? headBlockNumber - _maxCatchupBlocks : 0;
                _logger.LogInformation("No DB history. Starting from block: {Block} (max {Max} blocks from head {Head})", 
                    _lastSyncedBlock, _maxCatchupBlocks, headBlockNumber);
            }
        }

        private async Task PerformLimitedCatchupAsync(CancellationToken stoppingToken)
        {
            var currentHead = await GetCurrentBlockNumberWithRetryAsync(stoppingToken);
            if (currentHead == null)
            {
                _logger.LogWarning("Cannot perform catchup - failed to get current block number");
                return;
            }
            
            var headBlockNumber = (ulong)currentHead.Value;
            
            if (_lastSyncedBlock >= headBlockNumber)
            {
                _logger.LogInformation("Already up to date at block {Block}", _lastSyncedBlock);
                return;
            }

            var startBlock = _lastSyncedBlock + 1;
            var endBlock = headBlockNumber;
            var totalBlocks = endBlock - startBlock + 1;

            // Additional safety check - never scan more than 200 blocks
            if (totalBlocks > _maxCatchupBlocks)
            {
                startBlock = headBlockNumber - _maxCatchupBlocks + 1;
                totalBlocks = _maxCatchupBlocks;
                _logger.LogWarning("Limiting catchup to {Max} blocks. Starting from block {Start}", 
                    _maxCatchupBlocks, startBlock);
            }

            _logger.LogInformation("Starting limited catchup from block {Start} to {End} ({Total} blocks)", 
                startBlock, endBlock, totalBlocks);

            // Process in batches
            for (var batchStart = startBlock; batchStart <= endBlock; batchStart += (ulong)_batchSize)
            {
                stoppingToken.ThrowIfCancellationRequested();
                
                var batchEnd = Math.Min(batchStart + (ulong)_batchSize - 1, endBlock);
                
                _logger.LogDebug("Processing batch: blocks {BatchStart} to {BatchEnd}", batchStart, batchEnd);
                
                var batchTasks = new List<Task>();
                for (var block = batchStart; block <= batchEnd; block++)
                {
                    batchTasks.Add(ProcessBlockWithExtraRetryAsync(block, stoppingToken));
                }
                
                await Task.WhenAll(batchTasks);
                
                // Update _lastSyncedBlock to the highest processed block in this batch
                _lastSyncedBlock = batchEnd;
                
                // Small delay between batches
                if (batchEnd < endBlock)
                {
                    await Task.Delay(100, stoppingToken);
                }
            }

            _logger.LogInformation("Limited catchup completed. Now monitoring from block {Block}", _lastSyncedBlock);
        }

        private async Task ProcessBlockWithExtraRetryAsync(ulong blockNumber, CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    await ProcessBlockAsync(blockNumber, cancellationToken);
                    return;
                }
                catch (Exception ex) when (attempt < _maxRetries)
                {
                    _logger.LogWarning(ex, "Block {Block} attempt {Attempt} failed, retrying...", blockNumber, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(_retryDelaySeconds * attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Block {Block} failed after {MaxRetries} attempts", blockNumber, _maxRetries);
                    throw; // Re-throw on final attempt
                }
            }
        }

        private async Task ProcessBlockAsync(ulong blockNumber, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
            
            // Check if we've already processed this block
            var existingTxCount = await context.Transactions
                .CountAsync(t => t.BlockNumber == blockNumber.ToString(), cancellationToken);
            
            if (existingTxCount > 0)
            {
                _logger.LogDebug("Block {Block} already processed, skipping", blockNumber);
                return;
            }

            var block = await _httpWeb3.Eth.Blocks.GetBlockWithTransactionsByNumber
                .SendRequestAsync(new HexBigInteger(blockNumber));

            if (block?.Transactions == null || !block.Transactions.Any())
            {
                _logger.LogDebug("Block {Block} has no transactions", blockNumber);
                return;
            }

            var walletMap = (await context.Wallets.ToListAsync(cancellationToken))
                .ToDictionary(w => w.Address.ToLowerInvariant());

            if (!walletMap.Any())
            {
                _logger.LogDebug("No wallets to monitor, skipping block {Block}", blockNumber);
                return;
            }

            var newTxs = new List<Transaction>();

            foreach (var tx in block.Transactions)
            {
                var from = tx.From?.ToLowerInvariant();
                var to = tx.To?.ToLowerInvariant();

                // Skip zero-value transactions unless they're contract interactions
                if (tx.Value.Value == 0 && string.IsNullOrEmpty(tx.Input?.ToString()))
                {
                    continue;
                }

                // Process sender if it's in our wallets (DEBIT)
                if (from != null && walletMap.ContainsKey(from))
                {
                    var wallet = walletMap[from];
                    if (!await context.Transactions.AnyAsync(t => t.TransactionHash == tx.TransactionHash && t.WalletId == wallet.Id, cancellationToken))
                    {
                        var amount = Web3.Convert.FromWei(tx.Value);
                        var receiver = tx.To ?? "Contract Creation";
                        var newTx = new Transaction
                        {
                            Id = Guid.NewGuid(),
                            TransactionHash = tx.TransactionHash,
                            SenderAddress = tx.From,
                            ReceiverAddress = tx.To,
                            Amount = amount,
                            BlockNumber = blockNumber.ToString(),
                            BlockchainReference = tx.BlockHash,
                            WalletId = wallet.Id,
                            Status = TransactionFunctions.DetermineTransactionStatus(tx, new HexBigInteger(blockNumber)),
                            Type = TransactionType.Debit,
                            Currency = CurrencyType.ETH,
                            Description = $"Sent {amount} ETH to {receiver}",
                            CreatedAt = DateTime.UtcNow,
                            Timestamp = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        newTxs.Add(newTx);
                    }
                }

                // Process receiver if it's in our wallets (CREDIT)
                if (to != null && walletMap.ContainsKey(to))
                {
                    var wallet = walletMap[to];
                    if (!await context.Transactions.AnyAsync(t => t.TransactionHash == tx.TransactionHash && t.WalletId == wallet.Id, cancellationToken))
                    {
                        var amount = Web3.Convert.FromWei(tx.Value);
                        var newTx = new Transaction
                        {
                            Id = Guid.NewGuid(),
                            TransactionHash = tx.TransactionHash,
                            SenderAddress = tx.From,
                            ReceiverAddress = tx.To,
                            Amount = amount,
                            BlockNumber = blockNumber.ToString(),
                            BlockchainReference = tx.BlockHash,
                            WalletId = wallet.Id,
                            Status = TransactionFunctions.DetermineTransactionStatus(tx, new HexBigInteger(blockNumber)),
                            Type = TransactionType.Credit,
                            Currency = CurrencyType.ETH,
                            Description = $"Received {amount} ETH from {tx.From}",
                            CreatedAt = DateTime.UtcNow,
                            Timestamp = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        newTxs.Add(newTx);
                    }
                }
            }

            if (newTxs.Any())
            {
                context.Transactions.AddRange(newTxs);
                await context.SaveChangesAsync(cancellationToken);
                await UpdateWalletBalancesAsync(context, newTxs, cancellationToken);
                
                _logger.LogInformation("Block {Block}: Added {Count} new transactions", blockNumber, newTxs.Count);
            }
            else
            {
                _logger.LogDebug("Block {Block}: No relevant transactions found", blockNumber);
            }
        }

        private async Task UpdateWalletBalancesAsync(WalletContext context, List<Transaction> transactions, CancellationToken cancellationToken)
        {
            var walletIds = transactions.Select(t => t.WalletId).Distinct().ToList();
            var wallets = await context.Wallets
                .Where(w => walletIds.Contains(w.Id))
                .ToListAsync(cancellationToken);

            foreach (var tx in transactions)
            {
                // Handle sender (debit)
                if (!string.IsNullOrEmpty(tx.SenderAddress))
                {
                    var sender = wallets.FirstOrDefault(w => w.Address.Equals(tx.SenderAddress, StringComparison.OrdinalIgnoreCase));
                    if (sender != null)
                    {
                        sender.Balance = Math.Max(0, sender.Balance - tx.Amount);
                        await AdjustBalanceRecordAsync(context, sender.Id, -tx.Amount, cancellationToken);
                    }
                }

                // Handle receiver (credit)
                if (!string.IsNullOrEmpty(tx.ReceiverAddress))
                {
                    var receiver = wallets.FirstOrDefault(w => w.Address.Equals(tx.ReceiverAddress, StringComparison.OrdinalIgnoreCase));
                    if (receiver != null)
                    {
                        receiver.Balance += tx.Amount;
                        await AdjustBalanceRecordAsync(context, receiver.Id, tx.Amount, cancellationToken);
                    }
                }
            }
            
            await context.SaveChangesAsync(cancellationToken);
        }

        private async Task AdjustBalanceRecordAsync(WalletContext context, Guid walletId, decimal change, CancellationToken cancellationToken)
        {
            var bal = await context.WalletBalances
                .FirstOrDefaultAsync(wb => wb.WalletId == walletId && wb.Currency == CurrencyType.ETH, cancellationToken);
            
            if (bal == null)
            {
                context.WalletBalances.Add(new WalletBalance
                {
                    Id = Guid.NewGuid(),
                    WalletId = walletId,
                    Currency = CurrencyType.ETH,
                    Balance = Math.Max(0, change),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                bal.Balance = Math.Max(0, bal.Balance + change);
                bal.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}