using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nethereum.Web3;
using WalletBackend.Data;
using WalletBackend.Models;
using WalletBackend.Models.Enums;
using WalletBackend.Models.Etherscan;
using WalletBackend.Services.Functions;
using Transaction = WalletBackend.Models.Transaction;

namespace WalletBackend.Services.WalletService;

public class WalletBalanceService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient;
    private readonly string? _etherscanApiKey;
    private readonly string _etherscanBaseUrl;
    private readonly TimeSpan _updateInterval;
    private readonly ILogger<WalletBalanceService> _logger;
    private readonly int _requestDelayMs;

    public WalletBalanceService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<WalletBalanceService> logger, HttpClient httpClient)
    {
        _scopeFactory = scopeFactory;
        _httpClient = httpClient;
        _etherscanApiKey = Environment.GetEnvironmentVariable("EtherKey");
        _etherscanBaseUrl = configuration.GetValue<string>("Etherscan:BaseUrl", "https://api-sepolia.etherscan.io/api");
        _updateInterval = TimeSpan.FromMinutes(2);
        _requestDelayMs = configuration.GetValue<int>("Etherscan:RequestDelayMs", 200);
        _logger = logger;

        if (string.IsNullOrEmpty(_etherscanApiKey))
        {
            throw new InvalidOperationException("Etherscan API key is required. Set 'Etherscan:ApiKey' in configuration.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // On startup, refresh transactions from stored hashes
        await RefreshStoredTransactionHashesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
                    var wallets = await context.Wallets.ToListAsync(stoppingToken);
                    
                    _logger.LogDebug("Processing {Count} wallets for balance updates and transaction sync", wallets.Count);

                    var updatedWallets = new List<Wallet>();
                    var allNewTransactions = new List<Transaction>();
                    
                    foreach (var wallet in wallets)
                    {
                        try
                        {
                            // Get balance from Etherscan
                            var newBalance = await GetWalletBalanceAsync(wallet.Address, stoppingToken);
                            
                            var balanceDifference = Math.Abs(wallet.Balance - newBalance);
                            var significantChange = balanceDifference > 0.000001m;
                            
                            _logger.LogTrace("Wallet {Address}: Current={Current}, New={New}, Diff={Diff}, Update={ShouldUpdate}", 
                                wallet.Address, wallet.Balance, newBalance, balanceDifference, significantChange);
                            
                            if (significantChange)
                            {
                                _logger.LogDebug("Balance changed for {Address}: {Old} -> {New}", 
                                    wallet.Address, wallet.Balance, newBalance);
                                    
                                wallet.Balance = newBalance;
                                wallet.UpdatedAt = DateTime.UtcNow;
                                updatedWallets.Add(wallet);
                            }

                            // Always check for missing transactions (regardless of balance change)
                            var missingTransactions = await FindAndCreateMissingTransactionsAsync(wallet, context, stoppingToken);
                            allNewTransactions.AddRange(missingTransactions);

                            // Rate limiting between requests
                            await Task.Delay(_requestDelayMs, stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to update wallet {Address}", wallet.Address);
                        }
                    }
                    
                    // Save all changes at once
                    if (updatedWallets.Count > 0 || allNewTransactions.Count > 0)
                    {
                        if (updatedWallets.Count > 0)
                        {
                            context.UpdateRange(updatedWallets);
                        }
                        
                        if (allNewTransactions.Count > 0)
                        {
                            context.Transactions.AddRange(allNewTransactions);
                        }
                        
                        await context.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation("Updated {WalletCount} wallet balances and created {TxCount} missing transactions", 
                            updatedWallets.Count, allNewTransactions.Count);
                    }
                    else
                    {
                        _logger.LogDebug("No wallet changes or missing transactions detected");
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error in wallet balance update cycle: {Message}", e.Message);
            }
            
            await Task.Delay(_updateInterval, stoppingToken);
        }
    }

    private async Task<List<Transaction>> FindAndCreateMissingTransactionsAsync(Wallet wallet, WalletContext context, CancellationToken cancellationToken)
    {
        var newTransactions = new List<Transaction>();
        
        try
        {
            _logger.LogDebug("Checking for missing transactions for wallet {Address}", wallet.Address);
            
            // Get all transactions from Etherscan for this wallet
            var etherscanTransactions = await GetWalletTransactionsAsync(wallet.Address, cancellationToken);
            
            if (!etherscanTransactions.Any())
            {
                _logger.LogDebug("No transactions found on Etherscan for wallet {Address}", wallet.Address);
                return newTransactions;
            }

            // Get existing transaction hashes from database for this wallet
            var existingHashes = await context.Transactions
                .Where(t => t.WalletId == wallet.Id)
                .Select(t => t.TransactionHash.ToLower())
                .ToHashSetAsync(cancellationToken);

            // Find transactions that exist on Etherscan but not in our database
            var missingEtherscanTransactions = etherscanTransactions
                .Where(ethTx => !existingHashes.Contains(ethTx.Hash.ToLower()))
                .ToList();

            if (!missingEtherscanTransactions.Any())
            {
                _logger.LogDebug("No missing transactions found for wallet {Address}", wallet.Address);
                return newTransactions;
            }

            _logger.LogInformation("Found {Count} missing transactions for wallet {Address}", 
                missingEtherscanTransactions.Count, wallet.Address);

            foreach (var ethTx in missingEtherscanTransactions)
            {
                try
                {
                    var transactions = CreateTransactionsFromEtherscan(ethTx, wallet);
                    newTransactions.AddRange(transactions);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create transaction from Etherscan data for hash {Hash}", ethTx.Hash);
                }
            }
            
            _logger.LogDebug("Created {Count} new transaction records for wallet {Address}", 
                newTransactions.Count, wallet.Address);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding missing transactions for wallet {Address}", wallet.Address);
        }
        
        return newTransactions;
    }

    private List<Transaction> CreateTransactionsFromEtherscan(EtherscanTransaction ethTx, Wallet wallet)
    {
        var transactions = new List<Transaction>();
        
        try
        {
            var amount = Web3.Convert.FromWei((BigInteger)decimal.Parse(ethTx.Value));
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(ethTx.TimeStamp).DateTime;
            var status = ethTx.IsError == "0" ? TransactionStatus.Successful : TransactionStatus.Failed;

            var isFromWallet = string.Equals(ethTx.From, wallet.Address, StringComparison.OrdinalIgnoreCase);
            var isToWallet = string.Equals(ethTx.To, wallet.Address, StringComparison.OrdinalIgnoreCase);

            _logger.LogDebug("Processing transaction {Hash} for wallet {Address}: From={From}, To={To}, IsFromWallet={IsFromWallet}, IsToWallet={IsToWallet}", 
                ethTx.Hash, wallet.Address, ethTx.From, ethTx.To, isFromWallet, isToWallet);

            // Create debit transaction if wallet is the sender
            if (isFromWallet)
            {
                var debitTx = new Transaction
                {
                    Id = Guid.NewGuid(),
                    TransactionHash = ethTx.Hash,
                    SenderAddress = ethTx.From,
                    ReceiverAddress = ethTx.To,
                    Amount = amount,
                    BlockNumber = ethTx.BlockNumber,
                    BlockchainReference = ethTx.BlockHash,
                    WalletId = wallet.Id,
                    Status = status,
                    Type = TransactionType.Debit,
                    Currency = CurrencyType.ETH,
                    Description = $"Sent {amount} ETH to {ethTx.To ?? "Contract Creation"}",
                    CreatedAt = DateTime.UtcNow,
                    Timestamp = timestamp,
                    UpdatedAt = DateTime.UtcNow
                };
                transactions.Add(debitTx);
                _logger.LogDebug("Created debit transaction for hash {Hash}", ethTx.Hash);
            }

            // Create credit transaction if wallet is the receiver
            if (isToWallet)
            {
                var creditTx = new Transaction
                {
                    Id = Guid.NewGuid(),
                    TransactionHash = ethTx.Hash,
                    SenderAddress = ethTx.From,
                    ReceiverAddress = ethTx.To,
                    Amount = amount,
                    BlockNumber = ethTx.BlockNumber,
                    BlockchainReference = ethTx.BlockHash,
                    WalletId = wallet.Id,
                    Status = status,
                    Type = TransactionType.Credit,
                    Currency = CurrencyType.ETH,
                    Description = $"Received {amount} ETH from {ethTx.From}",
                    CreatedAt = DateTime.UtcNow,
                    Timestamp = timestamp,
                    UpdatedAt = DateTime.UtcNow
                };
                transactions.Add(creditTx);
                _logger.LogDebug("Created credit transaction for hash {Hash}", ethTx.Hash);
            }

            if (!transactions.Any())
            {
                _logger.LogWarning("No transactions created for hash {Hash} - wallet {Address} is neither sender nor receiver", 
                    ethTx.Hash, wallet.Address);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating transaction from Etherscan data for hash {Hash}", ethTx.Hash);
        }

        return transactions;
    }

    private async Task<decimal> GetWalletBalanceAsync(string address, CancellationToken cancellationToken)
    {
        Console.WriteLine("Etherscan API Key: " + _etherscanApiKey);
        Console.WriteLine("Etherscan API Key: " + _etherscanBaseUrl);


        var url = $"{_etherscanBaseUrl}?module=account&action=balance&address={address}&tag=latest&apikey={_etherscanApiKey}";
        _logger.LogTrace("Getting balance for address: {Address}", address);
        
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        
        try
        {
            var response = await _httpClient.GetStringAsync(url, combinedCts.Token);
            _logger.LogTrace("Etherscan balance response for {Address}: {Response}", address, response);
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var etherscanResponse = JsonSerializer.Deserialize<EtherscanBalanceResponse>(response, options);
            
            if (etherscanResponse == null)
            {
                throw new InvalidOperationException($"Failed to deserialize Etherscan response: {response}");
            }
            
            if (etherscanResponse.Status != "1")
            {
                if (string.IsNullOrEmpty(etherscanResponse.Message))
                {
                    throw new InvalidOperationException($"Etherscan API error: Unknown error. Status: {etherscanResponse.Status}, Response: {response}");
                }
                
                if (etherscanResponse.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Etherscan rate limit hit for address {Address}", address);
                    throw new InvalidOperationException($"Etherscan rate limit exceeded: {etherscanResponse.Message}");
                }
                
                throw new InvalidOperationException($"Etherscan API error: {etherscanResponse.Message}");
            }
            
            if (string.IsNullOrEmpty(etherscanResponse.Result))
            {
                throw new InvalidOperationException($"Etherscan API returned empty result for address {address}");
            }
            
            // Convert from Wei to ETH
            if (!decimal.TryParse(etherscanResponse.Result, out var balanceWei))
            {
                throw new InvalidOperationException($"Failed to parse balance result: {etherscanResponse.Result}");
            }
            
            return Web3.Convert.FromWei((BigInteger)balanceWei);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling Etherscan API for address {Address}", address);
            throw new InvalidOperationException($"Failed to call Etherscan API: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout calling Etherscan API for address {Address}", address);
            throw new InvalidOperationException("Etherscan API request timed out", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Etherscan response for address {Address}", address);
            throw new InvalidOperationException($"Failed to parse Etherscan response: {ex.Message}", ex);
        }
    }

    private async Task<List<EtherscanTransaction>> GetWalletTransactionsAsync(string address, CancellationToken cancellationToken)
    {
        var url = $"{_etherscanBaseUrl}?module=account&action=txlist&address={address}&startblock=0&endblock=99999999&page=1&offset=1000&sort=desc&apikey={_etherscanApiKey}";
        
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        
        try
        {
            var response = await _httpClient.GetStringAsync(url, combinedCts.Token);
            _logger.LogTrace("Etherscan transactions response for {Address}: {Response}", address, response);
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };
            
            var etherscanResponse = JsonSerializer.Deserialize<EtherscanTransactionResponse>(response, options);
            
            if (etherscanResponse == null)
            {
                _logger.LogWarning("Failed to deserialize Etherscan transaction response for {Address}: {Response}", address, response);
                return new List<EtherscanTransaction>();
            }
            
            if (etherscanResponse.Status != "1")
            {
                if (etherscanResponse.Message?.Contains("No transactions found", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.LogDebug("No transactions found for address {Address}", address);
                    return new List<EtherscanTransaction>();
                }
                
                if (etherscanResponse.Message?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _logger.LogWarning("Etherscan rate limit hit for transactions request for address {Address}", address);
                    throw new InvalidOperationException($"Etherscan rate limit exceeded: {etherscanResponse.Message}");
                }
                
                _logger.LogWarning("Etherscan API error for transactions request for {Address}: {Message}", address, etherscanResponse.Message);
                return new List<EtherscanTransaction>();
            }
            
            var transactions = etherscanResponse.Result ?? new List<EtherscanTransaction>();
            _logger.LogDebug("Retrieved {Count} transactions from Etherscan for address {Address}", transactions.Count, address);
            
            return transactions;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling Etherscan transactions API for address {Address}", address);
            return new List<EtherscanTransaction>();
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout calling Etherscan transactions API for address {Address}", address);
            return new List<EtherscanTransaction>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Etherscan transactions response for address {Address}.", address);
            return new List<EtherscanTransaction>();
        }
    }

    private async Task RefreshStoredTransactionHashesAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Refreshing stored transaction hashes on startup...");
        
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WalletContext>();
            
            var pendingHashes = await context.PendingTransactionHashes
                .Where(p => !p.IsProcessed)
                .ToListAsync(stoppingToken);
            
            if (!pendingHashes.Any())
            {
                _logger.LogInformation("No pending transaction hashes to process");
                return;
            }
            
            _logger.LogInformation("Processing {Count} pending transaction hashes", pendingHashes.Count);
            
            var processedCount = 0;
            var newTransactions = new List<Transaction>();
            
            foreach (var pendingHash in pendingHashes)
            {
                try
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    
                    // Check if transaction already exists
                    var existingTx = await context.Transactions
                        .FirstOrDefaultAsync(t => t.TransactionHash == pendingHash.TransactionHash, stoppingToken);
                    
                    if (existingTx != null)
                    {
                        pendingHash.IsProcessed = true;
                        processedCount++;
                        continue;
                    }
                    
                    // Get transaction details from Etherscan
                    var txDetails = await GetTransactionDetailsAsync(pendingHash.TransactionHash, stoppingToken);
                    
                    if (txDetails == null)
                    {
                        _logger.LogWarning("Transaction details not found for hash {Hash}", pendingHash.TransactionHash);
                        continue;
                    }
                    
                    // Find the wallet this transaction belongs to
                    var wallet = await context.Wallets
                        .FirstOrDefaultAsync(w => w.Address.ToLower() == pendingHash.WalletAddress.ToLower(), stoppingToken);
                    
                    if (wallet == null)
                    {
                        _logger.LogWarning("Wallet not found for address {Address}", pendingHash.WalletAddress);
                        pendingHash.IsProcessed = true;
                        continue;
                    }
                    
                    // Create transaction records using the same logic
                    var ethTx = new EtherscanTransaction
                    {
                        Hash = txDetails.Hash,
                        From = txDetails.From,
                        To = txDetails.To,
                        Value = txDetails.Value,
                        BlockNumber = txDetails.BlockNumber,
                        BlockHash = txDetails.BlockHash,
                        TimeStamp = txDetails.TimeStamp,
                        IsError = txDetails.IsError ?? "0"
                    };
                    
                    var transactions = CreateTransactionsFromEtherscan(ethTx, wallet);
                    newTransactions.AddRange(transactions);
                    
                    pendingHash.IsProcessed = true;
                    processedCount++;
                    
                    // Rate limiting
                    await Task.Delay(_requestDelayMs, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process transaction hash {Hash}", pendingHash.TransactionHash);
                }
            }
            
            // Save all changes
            if (newTransactions.Any())
            {
                context.Transactions.AddRange(newTransactions);
            }
            
            context.UpdateRange(pendingHashes.Where(p => p.IsProcessed));
            
            await context.SaveChangesAsync(stoppingToken);
            
            _logger.LogInformation("Processed {ProcessedCount} transaction hashes, created {NewTxCount} new transactions", 
                processedCount, newTransactions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing stored transaction hashes");
        }
    }

    private async Task<EtherscanTransaction> GetTransactionDetailsAsync(string txHash, CancellationToken cancellationToken)
    {
        var url = $"{_etherscanBaseUrl}?module=proxy&action=eth_getTransactionByHash&txhash={txHash}&apikey={_etherscanApiKey}";
        
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        
        try
        {
            var response = await _httpClient.GetStringAsync(url, combinedCts.Token);
            _logger.LogTrace("Etherscan transaction details response for {TxHash}: {Response}", txHash, response);
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var etherscanResponse = JsonSerializer.Deserialize<EtherscanTransactionDetailResponse>(response, options);
            
            return etherscanResponse?.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transaction details for hash {TxHash}", txHash);
            return null;
        }
    }
}