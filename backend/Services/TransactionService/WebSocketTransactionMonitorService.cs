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
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Reactive.Eth.Subscriptions;
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
    public class WebSocketTransactionMonitorService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly string _httpNodeUrl;
        private readonly string _webSocketNodeUrl;
        private readonly ILogger<WebSocketTransactionMonitorService> _logger;
        private readonly Web3 _httpWeb3;
        private ulong _lastSyncedBlock;
        private readonly int _blocksToScanThreshold;
        private readonly int _rpcTimeoutSeconds;
        private readonly int _maxRetries;
        private readonly int _retryDelaySeconds;

        public WebSocketTransactionMonitorService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<WebSocketTransactionMonitorService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            _httpNodeUrl = configuration["Ethereum:NodeUrl"]
                ?? throw new ArgumentException("Ethereum:NodeUrl must be set");
            _webSocketNodeUrl = configuration["Ethereum:WebSocketUrl"]
                ?? throw new ArgumentException("Ethereum:WebSocketUrl must be set");

            _blocksToScanThreshold = configuration.GetValue<int>("Blockchain:MaxCatchupBlocks", 50);
            _rpcTimeoutSeconds     = configuration.GetValue<int>("Blockchain:RpcTimeoutSeconds", 30);
            _maxRetries            = configuration.GetValue<int>("Blockchain:MaxRetries", 5);
            _retryDelaySeconds     = configuration.GetValue<int>("Blockchain:RetryDelaySeconds", 3);

            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                MaxConnectionsPerServer  = 5
            };
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(_rpcTimeoutSeconds)
            };
            _httpWeb3 = new Web3(new RpcClient(new Uri(_httpNodeUrl), httpClient));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting WebSocketTransactionMonitorService...");

            await InitializeBlockScanPointAsync(stoppingToken);
            await QuickCatchupIfNeededAsync(stoppingToken);
            await SetupWebSocketSubscriptionsAsync(stoppingToken);
        }

        private async Task InitializeBlockScanPointAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WalletContext>();

            var lastTx = await context.Transactions
                .OrderByDescending(t => t.BlockNumber)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastTx != null && ulong.TryParse(lastTx.BlockNumber, out var bn))
            {
                _lastSyncedBlock = bn;
                _logger.LogInformation("Resuming from DB last block: {Block}", _lastSyncedBlock);
            }
            else
            {
                var head = await _httpWeb3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                var block = (long)head.Value - _blocksToScanThreshold;
                _lastSyncedBlock = (ulong)Math.Max(0, block);

                _logger.LogInformation("No DB history. Starting at block: {Block}", _lastSyncedBlock);
            }
        }

        private async Task QuickCatchupIfNeededAsync(CancellationToken cancellationToken)
        {
            var head = await _httpWeb3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            var gap = (ulong)head.Value - _lastSyncedBlock;
            if (gap > (ulong)_blocksToScanThreshold)
            {
                _logger.LogInformation("Gap of {Gap} blocks > threshold {Threshold}, performing historical sync...", gap, _blocksToScanThreshold);
                await PerformReliableHistoricalSyncAsync(cancellationToken);
            }
            else
            {
                _logger.LogInformation("Gap of {Gap} blocks <= threshold, skipping historical sync.", gap);
            }
        }

        private async Task SetupWebSocketSubscriptionsAsync(CancellationToken cancellationToken)
        {
            var wsClient = new StreamingWebSocketClient(_webSocketNodeUrl);
            var headSub  = new EthNewBlockHeadersObservableSubscription(wsClient);

            headSub.GetSubscriptionDataResponsesAsObservable().Subscribe(
                async header =>
                {
                    var blockNumber = (ulong)header.Number.Value;
                    if (blockNumber > _lastSyncedBlock)
                    {
                        _logger.LogInformation("New block {Block} received", blockNumber);
                        await ProcessBlockWithExtraRetryAsync(blockNumber, cancellationToken);
                        _lastSyncedBlock = blockNumber;
                    }
                }, ex => _logger.LogError(ex, "Head subscription error"));

            var logSub = new EthLogsObservableSubscription(wsClient);
            var walletAddresses = await LoadWalletAddressesAsync();
            await logSub.SubscribeAsync(new NewFilterInput
            {
                FromBlock = new BlockParameter(new HexBigInteger(_lastSyncedBlock + 1)),
                ToBlock   = BlockParameter.CreateLatest(),
                Address   = walletAddresses.ToArray()
            });
            logSub.GetSubscriptionDataResponsesAsObservable().Subscribe(async log =>
            {
                await SaveLogAsTransactionAsync(log);
            });

            await wsClient.StartAsync();
            await headSub.SubscribeAsync();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Cancellation requested, stopping subscriptions");
            }
            finally
            {
                await headSub.UnsubscribeAsync();
                await logSub.UnsubscribeAsync();
                await wsClient.StopAsync();
            }
        }

        private async Task PerformReliableHistoricalSyncAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
            var latestBlock = await _httpWeb3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

            var startBlock = _lastSyncedBlock + 1;
            var endBlock   = (ulong)latestBlock.Value;

            for (var block = startBlock; block <= endBlock; block++)
            {
                await ProcessBlockWithExtraRetryAsync(block, stoppingToken);
            }
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
                    _logger.LogWarning(ex, "Block {Block} attempt {Attempt} failed", blockNumber, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(_retryDelaySeconds * attempt), cancellationToken);
                }
            }
            _logger.LogError("Block {Block} failed after {MaxRetries} attempts, skipping", blockNumber, _maxRetries);
        }

        private async Task ProcessBlockAsync(ulong blockNumber, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
            var block   = await _httpWeb3.Eth.Blocks.GetBlockWithTransactionsByNumber
                .SendRequestAsync(new HexBigInteger(blockNumber));

            if (block?.Transactions == null) return;

            var walletMap = (await context.Wallets.ToListAsync(cancellationToken))
                .ToDictionary(w => w.Address.ToLowerInvariant());

            var newTxs = new List<Transaction>();
            foreach (var tx in block.Transactions)
            {
                var from = tx.From?.ToLowerInvariant();
                var to   = tx.To?.ToLowerInvariant();

                // Process sender if it's in our wallets
                if (from != null && walletMap.ContainsKey(from))
                {
                    var wallet = walletMap[from];
                    if (!await context.Transactions.AnyAsync(t => t.TransactionHash == tx.TransactionHash && t.WalletId == wallet.Id, cancellationToken))
                    {
                        var amount = Web3.Convert.FromWei(tx.Value);
                        var receiver = tx.To != null ? tx.To : "contract creation";
                        var newTx = new Transaction
                        {
                            Id                 = Guid.NewGuid(),
                            TransactionHash    = tx.TransactionHash,
                            SenderAddress      = tx.From,
                            ReceiverAddress    = tx.To, // Can be null for contract creation
                            Amount             = amount,
                            BlockNumber        = blockNumber.ToString(),
                            BlockchainReference= tx.BlockHash,
                            WalletId           = wallet.Id,
                            Status             = TransactionFunctions.DetermineTransactionStatus(tx, new HexBigInteger(blockNumber)),
                            Type               = TransactionType.Debit,
                            Currency           = CurrencyType.ETH,
                            Description        = $"Sent {amount} ETH to {receiver}",
                            CreatedAt          = DateTime.UtcNow,
                            Timestamp          = DateTime.UtcNow,
                            UpdatedAt          = DateTime.UtcNow
                        };
                        newTxs.Add(newTx);
                    }
                }

                // Process receiver if it's in our wallets and 'to' is not null
                if (to != null && walletMap.ContainsKey(to))
                {
                    var wallet = walletMap[to];
                    if (!await context.Transactions.AnyAsync(t => t.TransactionHash == tx.TransactionHash && t.WalletId == wallet.Id, cancellationToken))
                    {
                        var amount = Web3.Convert.FromWei(tx.Value);
                        var newTx = new Transaction
                        {
                            Id                 = Guid.NewGuid(),
                            TransactionHash    = tx.TransactionHash,
                            SenderAddress      = tx.From,
                            ReceiverAddress    = tx.To,
                            Amount             = amount,
                            BlockNumber        = blockNumber.ToString(),
                            BlockchainReference= tx.BlockHash,
                            WalletId           = wallet.Id,
                            Status             = TransactionFunctions.DetermineTransactionStatus(tx, new HexBigInteger(blockNumber)),
                            Type               = TransactionType.Credit,
                            Currency           = CurrencyType.ETH,
                            Description        = $"Received {amount} ETH from {tx.From}",
                            CreatedAt          = DateTime.UtcNow,
                            Timestamp          = DateTime.UtcNow,
                            UpdatedAt          = DateTime.UtcNow
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
            }
        }

        private async Task UpdateWalletBalancesAsync(WalletContext context, List<Transaction> transactions, CancellationToken cancellationToken)
        {
            var wallets = await context.Wallets.ToListAsync(cancellationToken);
            foreach (var tx in transactions)
            {
                if (!string.IsNullOrEmpty(tx.SenderAddress))
                {
                    var sender = wallets.FirstOrDefault(w => w.Address.Equals(tx.SenderAddress, StringComparison.OrdinalIgnoreCase));
                    if (sender != null)
                    {
                        sender.Balance -= tx.Amount;
                        await AdjustBalanceRecordAsync(context, sender.Id, -tx.Amount, cancellationToken);
                    }
                }
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
            var bal = await context.WalletBalances.FirstOrDefaultAsync(wb => wb.WalletId == walletId && wb.Currency == CurrencyType.ETH, cancellationToken);
            if (bal == null)
            {
                context.WalletBalances.Add(new WalletBalance
                {
                    Id         = Guid.NewGuid(),
                    WalletId   = walletId,
                    Currency   = CurrencyType.ETH,
                    Balance    = Math.Max(0, change),
                    CreatedAt  = DateTime.UtcNow,
                    UpdatedAt  = DateTime.UtcNow
                });
            }
            else
            {
                bal.Balance   += change;
                bal.UpdatedAt = DateTime.UtcNow;
            }
        }

        private async Task<List<string>> LoadWalletAddressesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
            return await context.Wallets.Select(w => w.Address.ToLowerInvariant()).ToListAsync();
        }

        private async Task SaveLogAsTransactionAsync(FilterLog log)
        {
            // Placeholder for future implementation to handle ERC20 or contract event logs
        }
    }
}