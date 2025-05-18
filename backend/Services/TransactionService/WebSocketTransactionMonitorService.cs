using Microsoft.EntityFrameworkCore;
using Nethereum.Web3;
using WalletBackend.Data;

using WalletBackend.Services.Functions;

using System.Numerics;

using System.Threading.Channels;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.WebSocketStreamingClient;
using Nethereum.RPC.Reactive.Eth.Subscriptions;


namespace WalletBackend.Services.TransactionService;

public class WebSocketTransactionMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _nodeUrl;
    private readonly ILogger<WebSocketTransactionMonitorService> _logger;
    private ulong _lastSyncedBlock = 0;
    private readonly int _blocksToScan;
    private readonly Channel<ulong> _blockQueue = Channel.CreateUnbounded<ulong>();

    public WebSocketTransactionMonitorService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<WebSocketTransactionMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _nodeUrl = configuration["Ethereum:WebSocketUrl"];
        _blocksToScan = configuration.GetValue<int>("Blockchain:BlocksToScan", 1000);
        _logger.LogInformation("WebSocketTransactionMonitorService initialized. Will scan {BlockCount} historical blocks.", _blocksToScan);
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
            var web3 = new Web3(_nodeUrl);

            var currentBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
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
            var web3 = new Web3(_nodeUrl);
            var currentBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            _lastSyncedBlock = (ulong)BigInteger.Max((BigInteger)0, currentBlock.Value - 100);
            _logger.LogInformation("Defaulting to start scan from block {BlockNumber}", _lastSyncedBlock);
        }
    }

    private async Task PerformInitialSyncAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
        var web3 = new Web3(_nodeUrl);

        var latestBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
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

        for (var blockNumber = startBlock; blockNumber <= endBlock; blockNumber++)
        {
            await ProcessBlockAsync(blockNumber, stoppingToken);
            _lastSyncedBlock = blockNumber;
        }

        _logger.LogInformation("Initial sync completed up to block {BlockNumber}", _lastSyncedBlock);
    }

    private async Task SetupWebSocketSubscriptionAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var client = new StreamingWebSocketClient(_nodeUrl))
                {
                    var subscription = new EthNewBlockHeadersObservableSubscription(client);
                    subscription.GetSubscriptionDataResponsesAsObservable().Subscribe(blockHeader =>
                    {
                        var blockNumber = blockHeader.Number.Value;
                        if (blockNumber > _lastSyncedBlock)
                        {
                            ulong blockNumberUlong = (ulong)blockNumber;
                            _blockQueue.Writer.TryWrite(blockNumberUlong);
                        }
                    });

                    await client.StartAsync();
                    await subscription.SubscribeAsync();

                    // Keep the subscription running until cancellation is requested
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Service is stopping
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in WebSocket subscription. Retrying in 30 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task ProcessBlocksAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var blockNumber = await _blockQueue.Reader.ReadAsync(stoppingToken);
                if (blockNumber > _lastSyncedBlock)
                {
                    await ProcessBlockAsync(blockNumber, stoppingToken);
                    _lastSyncedBlock = blockNumber;
                }
            }
            catch (OperationCanceledException)
            {
                // Service is stopping
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing block from queue");
            }
        }
    }

    private async Task ProcessBlockAsync(ulong blockNumber, CancellationToken stoppingToken)
{
    using var scope = _scopeFactory.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
    var web3 = new Web3(_nodeUrl);

    try
    {
        var blockWithTxs = await web3.Eth.Blocks
            .GetBlockWithTransactionsByNumber.SendRequestAsync(new HexBigInteger(blockNumber));

        if (blockWithTxs?.Transactions == null || !blockWithTxs.Transactions.Any())
        {
            _logger.LogInformation("Block {BlockNumber} has no transactions or couldn't be fetched.", blockNumber);
            return;
        }

        // 1) Cache wallets once per block
        var wallets = await context.Wallets.ToListAsync(stoppingToken);
        var walletLookup = wallets
            .ToDictionary(w => w.Address.ToLower(), w => w);

        if (!walletLookup.Any())
        {
            _logger.LogWarning("No wallet addresses found to monitor for block {BlockNumber}", blockNumber);
            return;
        }

        var newTransactions = new List<Models.Transaction>();
        int newTransactionsFound = 0;

        // 2) Loop through txs and build a list of new ones
        foreach (var tx in blockWithTxs.Transactions)
        {
            if (tx == null) continue;

            var fromLower = tx.From?.ToLower();
            var toLower   = tx.To?.ToLower();

            // only continue if it's from or to one of our wallets
            if ((fromLower != null && walletLookup.ContainsKey(fromLower)) ||
                (toLower   != null && walletLookup.ContainsKey(toLower)))
            {
                // skip if already in DB
                var exists = await context.Transactions
                    .AnyAsync(t => t.TransactionHash == tx.TransactionHash, stoppingToken);
                if (exists) continue;

                // pick the matching wallet
                var wallet = fromLower != null && walletLookup.TryGetValue(fromLower, out var w1)
                             ? w1
                             : toLower != null && walletLookup.TryGetValue(toLower, out var w2)
                               ? w2
                               : null;

                if (wallet == null) continue;

                newTransactions.Add(new Models.Transaction
                {
                    TransactionHash     = tx.TransactionHash,
                    SenderAddress       = tx.From,
                    ReceiverAddress     = tx.To,
                    Amount              = Web3.Convert.FromWei(tx.Value),
                    BlockNumber         = blockNumber.ToString(),
                    BlockchainReference = tx.BlockHash,
                    WalletId            = wallet.Id,
                    Status              = TransactionFunctions.DetermineTransactionStatus(tx, new HexBigInteger(blockNumber)),
                    Type                = TransactionFunctions.DetermineTransactionType(tx, walletLookup.Keys.ToList()),
                    CreatedAt           = DateTime.UtcNow,
                    Description         = TransactionFunctions.GenerateTransactionDescription(tx, walletLookup.Keys.ToList())
                });
                newTransactionsFound++;
            }
        }

        // 3) Bulk insert and save once
        if (newTransactions.Any())
        {
            context.Transactions.AddRange(newTransactions);
            await context.SaveChangesAsync(stoppingToken);
        }

        _logger.LogInformation("Processed block {BlockNumber}, found {Count} new transactions", blockNumber, newTransactionsFound);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error processing block {BlockNumber}", blockNumber);
    }
}

}