using Microsoft.EntityFrameworkCore;
using Nethereum.Web3;
using WalletBackend.Data;
using WalletBackend.Services.Functions;
using System.Numerics;
using System.Threading.Channels;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Reactive.Eth.Subscriptions;
using WalletBackend.Models;
using WalletBackend.Models.Enums;
using Nethereum.JsonRpc.Client;
using System.Net.Http;

namespace WalletBackend.Services.TransactionService;

public class WebSocketTransactionMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _httpNodeUrl;
    private readonly string _webSocketNodeUrl;
    private readonly ILogger<WebSocketTransactionMonitorService> _logger;
    private ulong _lastSyncedBlock = 0;
    private readonly int _blocksToScan;
    private readonly Channel<ulong> _blockQueue = Channel.CreateUnbounded<ulong>();
    private readonly int _rpcTimeoutSeconds;
    private readonly int _maxRetries;
    private readonly int _retryDelaySeconds;
    private readonly SemaphoreSlim _blockProcessingSemaphore;
    private readonly int _maxConcurrentBlocks;

    public WebSocketTransactionMonitorService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<WebSocketTransactionMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        
        // Separate HTTP and WebSocket URLs
        _httpNodeUrl = configuration["Ethereum:HttpUrl"] ?? 
                      configuration["Ethereum:WebSocketUrl"]?.Replace("ws://", "http://").Replace("wss://", "https://") ??
                      throw new ArgumentException("Either Ethereum:HttpUrl or Ethereum:WebSocketUrl must be configured");
        
        _webSocketNodeUrl = configuration["Ethereum:WebSocketUrl"] ?? 
                           throw new ArgumentException("Ethereum:WebSocketUrl must be configured for real-time notifications");
        
        _blocksToScan = configuration.GetValue<int>("Blockchain:BlocksToScan", 1000);
        _rpcTimeoutSeconds = configuration.GetValue<int>("Blockchain:RpcTimeoutSeconds", 60);
        _maxRetries = configuration.GetValue<int>("Blockchain:MaxRetries", 3);
        _retryDelaySeconds = configuration.GetValue<int>("Blockchain:RetryDelaySeconds", 5);
        _maxConcurrentBlocks = configuration.GetValue<int>("Blockchain:MaxConcurrentBlocks", 3);
        
        _blockProcessingSemaphore = new SemaphoreSlim(_maxConcurrentBlocks, _maxConcurrentBlocks);
        
        _logger.LogInformation("WebSocketTransactionMonitorService initialized. HTTP: {HttpUrl}, WebSocket: {WsUrl}, " +
                             "Will scan {BlockCount} historical blocks. RPC timeout: {TimeoutSeconds}s, Max retries: {MaxRetries}, " +
                             "Max concurrent blocks: {MaxConcurrent}",
            _httpNodeUrl, _webSocketNodeUrl, _blocksToScan, _rpcTimeoutSeconds, _maxRetries, _maxConcurrentBlocks);
    }

    private Web3 CreateHttpWeb3Instance()
    {
        var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(_rpcTimeoutSeconds);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "WalletBackend/1.0");
        
        var rpcClient = new RpcClient(new Uri(_httpNodeUrl), httpClient);
        return new Web3(rpcClient);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WebSocketTransactionMonitorService started");

        // Initialize the last synced block
        await InitializeBlockScanPointAsync(stoppingToken);

        // Perform initial sync to catch up to the current block
        await PerformInitialSyncAsync(stoppingToken);

        // Start block processing in the background
        _ = ProcessBlocksAsync(stoppingToken);

        // Set up WebSocket subscription for real-time updates
        await SetupWebSocketSubscriptionAsync(stoppingToken);
    }

    private async Task InitializeBlockScanPointAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
            var web3 = CreateHttpWeb3Instance();

            var currentBlock = await ExecuteWithRetryAsync(async () => 
                await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync(), "GetBlockNumber");
                
            var lastTx = await context.Transactions
                .OrderByDescending(t => t.BlockNumber)
                .FirstOrDefaultAsync(stoppingToken);

            if (lastTx != null)
            {
                _lastSyncedBlock = ulong.Parse(lastTx.BlockNumber);
                _logger.LogInformation("Found existing transactions. Starting from block {BlockNumber}", _lastSyncedBlock);
            }
            else
            {
                _lastSyncedBlock = (ulong)BigInteger.Max((BigInteger)0, currentBlock.Value - _blocksToScan);
                _logger.LogInformation("No existing transactions. Starting historical scan from block {BlockNumber}", _lastSyncedBlock);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing block scan point");
            var web3 = CreateHttpWeb3Instance();
            var currentBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            _lastSyncedBlock = (ulong)BigInteger.Max((BigInteger)0, currentBlock.Value - 100);
            _logger.LogInformation("Defaulting to start scan from block {BlockNumber}", _lastSyncedBlock);
        }
    }

    private async Task PerformInitialSyncAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
        var web3 = CreateHttpWeb3Instance();

        var latestBlock = await ExecuteWithRetryAsync(async () => 
            await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync(), "GetBlockNumber");
            
        var startBlock = _lastSyncedBlock + 1;
        var endBlock = (ulong)latestBlock.Value;

        if (startBlock > endBlock)
        {
            _logger.LogInformation("Already up to date with latest block");
            return;
        }

        _logger.LogInformation("Performing initial sync from block {StartBlock} to {EndBlock}", startBlock, endBlock);

        var walletAddresses = await context.Wallets
            .Select(w => w.Address.ToLower())
            .ToListAsync(stoppingToken);

        if (walletAddresses.Count == 0)
        {
            _logger.LogWarning("No wallet addresses found to monitor");
            _lastSyncedBlock = endBlock;
            return;
        }

        // Process blocks in parallel but control concurrency
        var blockTasks = new List<Task>();
        var blockRange = Enumerable.Range((int)startBlock, (int)(endBlock - startBlock + 1))
            .Select(b => (ulong)b);

        foreach (var blockNumber in blockRange)
        {
            if (stoppingToken.IsCancellationRequested) break;

            await _blockProcessingSemaphore.WaitAsync(stoppingToken);
            
            var task = ProcessBlockWithSemaphoreAsync(blockNumber, stoppingToken);
            blockTasks.Add(task);
            
            // Update _lastSyncedBlock after processing (but we need to ensure order)
            // For initial sync, we'll process sequentially to maintain order
            await task;
            _lastSyncedBlock = blockNumber;
        }

        await Task.WhenAll(blockTasks);
        _logger.LogInformation("Initial sync completed up to block {BlockNumber}", _lastSyncedBlock);
    }

    private async Task ProcessBlockWithSemaphoreAsync(ulong blockNumber, CancellationToken stoppingToken)
    {
        try
        {
            await ProcessBlockAsync(blockNumber, stoppingToken);
        }
        finally
        {
            _blockProcessingSemaphore.Release();
        }
    }

    private async Task SetupWebSocketSubscriptionAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            StreamingWebSocketClient client = null;
            EthNewBlockHeadersObservableSubscription subscription = null;
            
            try
            {
                _logger.LogInformation("Establishing WebSocket connection to {Url}", _webSocketNodeUrl);
                client = new StreamingWebSocketClient(_webSocketNodeUrl);
                
                subscription = new EthNewBlockHeadersObservableSubscription(client);
                
                subscription.GetSubscriptionDataResponsesAsObservable().Subscribe(
                    blockHeader =>
                    {
                        try
                        {
                            var blockNumber = (ulong)blockHeader.Number.Value;
                            _logger.LogDebug("Received new block header: {BlockNumber}", blockNumber);
                            
                            if (blockNumber > _lastSyncedBlock)
                            {
                                // Queue block for processing
                                if (!_blockQueue.Writer.TryWrite(blockNumber))
                                {
                                    _logger.LogWarning("Failed to queue block {BlockNumber} for processing", blockNumber);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing block header notification");
                        }
                    },
                    error =>
                    {
                        _logger.LogError("WebSocket subscription error: {Error}", error.Message);
                    },
                    () =>
                    {
                        _logger.LogInformation("WebSocket subscription completed");
                    });

                await client.StartAsync();
                _logger.LogInformation("WebSocket client started successfully");
                
                await subscription.SubscribeAsync();
                _logger.LogInformation("Successfully subscribed to new block headers");

                // Keep the subscription running until cancellation is requested
                try
                {
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("WebSocket subscription cancelled");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Service is stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in WebSocket subscription. Retrying in 30 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            finally
            {
                // Clean up resources
                try
                {
                    if (subscription != null)
                    {
                        _logger.LogInformation("Unsubscribing from new block headers");
                        await subscription.UnsubscribeAsync();
                    }

                    if (client != null)
                    {
                        _logger.LogInformation("Stopping WebSocket client");
                        await client.StopAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing WebSocket resources");
                }
            }
        }
    }

    private async Task ProcessBlocksAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting block processing queue consumer");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Read from the queue with cancellation support
                var blockNumber = await _blockQueue.Reader.ReadAsync(stoppingToken);
                
                if (blockNumber > _lastSyncedBlock)
                {
                    _logger.LogInformation("Processing queued block {BlockNumber}", blockNumber);
                    
                    // Process block with semaphore to control concurrency
                    await _blockProcessingSemaphore.WaitAsync(stoppingToken);
                    
                    // Process in background to avoid blocking the queue
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessBlockAsync(blockNumber, stoppingToken);
                            
                            // Update last synced block (ensure thread-safety for this operation)
                            lock (this)
                            {
                                if (blockNumber > _lastSyncedBlock)
                                {
                                    _lastSyncedBlock = blockNumber;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing queued block {BlockNumber}", blockNumber);
                        }
                        finally
                        {
                            _blockProcessingSemaphore.Release();
                        }
                    }, stoppingToken);
                }
                else
                {
                    _logger.LogDebug("Skipping block {BlockNumber} as it's already processed (last synced: {LastSynced})", 
                        blockNumber, _lastSyncedBlock);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Block processing cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading from block queue");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName)
    {
        Exception lastException = null;
        
        for (int attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                _logger.LogDebug("Executing {OperationName}, attempt {Attempt}/{MaxRetries}", operationName, attempt, _maxRetries);
                return await operation();
            }
            catch (RpcClientTimeoutException ex)
            {
                lastException = ex;
                _logger.LogWarning("RPC timeout on {OperationName}, attempt {Attempt}/{MaxRetries}: {Message}", 
                    operationName, attempt, _maxRetries, ex.Message);
                
                if (attempt < _maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(_retryDelaySeconds * attempt); // Exponential backoff
                    _logger.LogInformation("Retrying {OperationName} in {DelaySeconds} seconds...", operationName, delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.Message.Contains("timeout"))
            {
                lastException = ex;
                _logger.LogWarning("Request timeout on {OperationName}, attempt {Attempt}/{MaxRetries}: {Message}", 
                    operationName, attempt, _maxRetries, ex.Message);
                
                if (attempt < _maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(_retryDelaySeconds * attempt);
                    _logger.LogInformation("Retrying {OperationName} in {DelaySeconds} seconds...", operationName, delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("timeout"))
            {
                lastException = ex;
                _logger.LogWarning("HTTP timeout on {OperationName}, attempt {Attempt}/{MaxRetries}: {Message}", 
                    operationName, attempt, _maxRetries, ex.Message);
                
                if (attempt < _maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(_retryDelaySeconds * attempt);
                    _logger.LogInformation("Retrying {OperationName} in {DelaySeconds} seconds...", operationName, delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during {OperationName}, attempt {Attempt}/{MaxRetries}", operationName, attempt, _maxRetries);
                throw; // Don't retry for non-timeout exceptions
            }
        }
        
        _logger.LogError(lastException, "All {MaxRetries} attempts failed for {OperationName}", _maxRetries, operationName);
        throw lastException ?? new Exception($"All retry attempts failed for {operationName}");
    }
    
    private async Task ProcessBlockAsync(ulong blockNumber, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
        var web3 = CreateHttpWeb3Instance(); // Use HTTP for block fetching

        try
        {
            _logger.LogInformation("Processing block {BlockNumber} via HTTP", blockNumber);

            // Use retry mechanism for fetching block data via HTTP
            var blockWithTxs = await ExecuteWithRetryAsync(async () => 
                await web3.Eth.Blocks.GetBlockWithTransactionsByNumber.SendRequestAsync(new HexBigInteger(blockNumber)),
                $"GetBlockWithTransactions-{blockNumber}");

            if (blockWithTxs?.Transactions == null || !blockWithTxs.Transactions.Any())
            {
                _logger.LogInformation("Block {BlockNumber} has no transactions", blockNumber);
                return;
            }

            _logger.LogInformation("Block {BlockNumber} has {TxCount} transactions (fetched via HTTP)", 
                blockNumber, blockWithTxs.Transactions.Length);

            // Cache wallets once per block
            var wallets = await context.Wallets.ToListAsync(stoppingToken);
            var walletLookup = wallets.ToDictionary(w => w.Address.ToLower(), w => w);

            if (!walletLookup.Any())
            {
                _logger.LogWarning("No wallet addresses found to monitor for block {BlockNumber}", blockNumber);
                return;
            }

            var newTransactions = new List<Models.Transaction>();
            int relevantTransactions = 0;

            // Process transactions
            foreach (var tx in blockWithTxs.Transactions)
            {
                if (tx == null || stoppingToken.IsCancellationRequested) continue;

                var fromLower = tx.From?.ToLower();
                var toLower = tx.To?.ToLower();

                var isFromOurWallet = fromLower != null && walletLookup.ContainsKey(fromLower);
                var isToOurWallet = toLower != null && walletLookup.ContainsKey(toLower);

                if (isFromOurWallet || isToOurWallet)
                {
                    relevantTransactions++;

                    // Check if transaction already exists
                    var exists = await context.Transactions
                        .AnyAsync(t => t.TransactionHash == tx.TransactionHash, stoppingToken);
                    
                    if (exists)
                    {
                        _logger.LogDebug("Transaction {TxHash} already exists, skipping", tx.TransactionHash);
                        continue;
                    }

                    // Select the appropriate wallet
                    var wallet = isToOurWallet && toLower != null && walletLookup.TryGetValue(toLower, out var receiverWallet)
                                ? receiverWallet
                                : isFromOurWallet && fromLower != null && walletLookup.TryGetValue(fromLower, out var senderWallet)
                                ? senderWallet
                                : null;

                    if (wallet == null) continue;

                    var amount = Web3.Convert.FromWei(tx.Value);

                    var newTransaction = new Models.Transaction
                    {
                        TransactionHash = tx.TransactionHash,
                        SenderAddress = tx.From,
                        ReceiverAddress = tx.To,
                        Amount = amount,
                        BlockNumber = blockNumber.ToString(),
                        BlockchainReference = tx.BlockHash,
                        WalletId = wallet.Id,
                        Status = TransactionFunctions.DetermineTransactionStatus(tx, new HexBigInteger(blockNumber)),
                        Type = TransactionFunctions.DetermineTransactionType(tx, walletLookup.Keys.ToList()),
                        CreatedAt = DateTime.UtcNow,
                        Description = TransactionFunctions.GenerateTransactionDescription(tx, walletLookup.Keys.ToList())
                    };

                    newTransactions.Add(newTransaction);
                }
            }

            _logger.LogInformation("Block {BlockNumber}: Found {RelevantCount} relevant, {NewCount} new transactions", 
                blockNumber, relevantTransactions, newTransactions.Count);

            // Save transactions and update balances
            if (newTransactions.Any())
            {
                // Process in smaller batches for better performance
                const int batchSize = 50;
                for (int i = 0; i < newTransactions.Count; i += batchSize)
                {
                    var batch = newTransactions.Skip(i).Take(batchSize).ToList();
                    context.Transactions.AddRange(batch);
                    await context.SaveChangesAsync(stoppingToken);
                }

                // Update wallet balances
                await UpdateWalletBalancesAsync(context, newTransactions, wallets, stoppingToken);

                _logger.LogInformation("Successfully processed block {BlockNumber} with {Count} new transactions", 
                    blockNumber, newTransactions.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing block {BlockNumber}", blockNumber);
            throw;
        }
    }

    private async Task UpdateWalletBalancesAsync(WalletContext context, List<Models.Transaction> transactions, 
        List<Wallet> wallets, CancellationToken stoppingToken)
    {
        foreach (var tx in transactions)
        {
            // Handle sender
            if (!string.IsNullOrEmpty(tx.SenderAddress))
            {
                var senderWallet = wallets.FirstOrDefault(w => w.Address.ToLower() == tx.SenderAddress.ToLower());
                if (senderWallet != null)
                {
                    senderWallet.Balance -= tx.Amount;
                    await UpdateWalletBalance(context, senderWallet.Id, -tx.Amount, stoppingToken);
                }
            }

            // Handle receiver
            if (!string.IsNullOrEmpty(tx.ReceiverAddress))
            {
                var receiverWallet = wallets.FirstOrDefault(w => w.Address.ToLower() == tx.ReceiverAddress.ToLower());
                if (receiverWallet != null)
                {
                    receiverWallet.Balance += tx.Amount;
                    await UpdateWalletBalance(context, receiverWallet.Id, tx.Amount, stoppingToken);
                }
            }
        }

        await context.SaveChangesAsync(stoppingToken);
    }

    private async Task UpdateWalletBalance(WalletContext context, Guid walletId, decimal amountChange, CancellationToken stoppingToken)
    {
        var walletBalance = await context.WalletBalances
            .FirstOrDefaultAsync(wb => wb.WalletId == walletId && wb.Currency == CurrencyType.ETH, stoppingToken);

        if (walletBalance == null)
        {
            // Create new balance record
            walletBalance = new WalletBalance
            {
                Id = Guid.NewGuid(),
                WalletId = walletId,
                Currency = CurrencyType.ETH,
                Balance = amountChange > 0 ? amountChange : 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.WalletBalances.Add(walletBalance);
        }
        else
        {
            walletBalance.Balance += amountChange;
            walletBalance.UpdatedAt = DateTime.UtcNow;
        }
    }

    public override void Dispose()
    {
        _blockProcessingSemaphore?.Dispose();
        base.Dispose();
    }
}