using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AutoMapper;
using CsvHelper;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nethereum.JsonRpc.Client;
using Nethereum.Model;
using Nethereum.Web3;
using WalletBackend.Data;
using WalletBackend.Models;
using WalletBackend.Services.WalletService;
using WalletBackend.Models.DTOS.Transaction;
using WalletBackend.Models.Enums;
using WalletBackend.Models.Responses;
using TransactionType = WalletBackend.Models.Enums.TransactionType;

namespace WalletBackend.Services.TransactionService;

 public class TransactionService : ITransactionService
    {
        private readonly WalletContext _context;
        private readonly string _nodeUrl;
        private readonly IMapper _mapper;
        private readonly ILogger<TransactionService> _logger;
        private readonly IWalletService _walletService;
        private readonly IWalletUnlockService _walletUnlockService;
        private readonly string? _ethKey;

        public TransactionService(IConfiguration configuration, ILogger<TransactionService> logger, IMapper mapper, WalletContext context, IWalletService walletService, IWalletUnlockService walletUnlockService)
        {
            _context = context;
            _mapper = mapper;
        _ethKey = Environment.GetEnvironmentVariable("EthereumKey");
            _walletService = walletService;
            _logger = logger;
            _nodeUrl = configuration.GetValue<string>("Ethereum:NodeUrl",  $"https://eth-sepolia.g.alchemy.com/v2/{_ethKey}");
            _walletUnlockService = walletUnlockService;  
          
        }
            
        
        public async Task<CreateTransactionModel> CreateTransactionAsync(CreateTransactionModel model)
        {
            var transaction = _mapper.Map<Models.Transaction>(model);
            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();
            return _mapper.Map<CreateTransactionModel>(transaction);
        }

        public async Task<TransactionResult> SendEthereumAsync(CurrencyTransactionRequest request)
{
    try
    {
        // 1. Validate receiver address
        if (!Nethereum.Util.AddressUtil.Current.IsValidEthereumAddressHexFormat(request.ReceiverAddress))
        {
            return new TransactionResult
            {
                Success = false,
                Message = $"Invalid receiver address: {request.ReceiverAddress}"
            };
        }

        // 2. Check if private key is accessible
        if (!_walletUnlockService.TryGetPrivateKey(request.WalletId, out var privateKey))
        {
            return new TransactionResult
            {
                Success = false,
                Message = "Wallet is locked. Please unlock your wallet first."
            };
        }

        _logger.LogInformation(_nodeUrl);
        
        var web3 = CreateWeb3(privateKey);


        // 3. Check ETH balance
        var balanceWei = await web3.Eth.GetBalance.SendRequestAsync(request.SenderAddress);
        var balanceEth = Web3.Convert.FromWei(balanceWei);

        if (balanceEth < request.Amount)
        {
            return new TransactionResult
            {
                Success = false,
                Message = $"Insufficient ETH balance. Wallet has {balanceEth} ETH, tried to send {request.Amount} ETH"
            };
        }

        // 4. Submit to Ethereum network
        var blockchainResponse = await SubmitToEthereumNetworkAsync(privateKey, request);

        _logger.LogInformation("Blockchain response JSON:\n{Json}",
            JsonSerializer.Serialize(blockchainResponse, new JsonSerializerOptions { WriteIndented = true }));

        // 5. Build the transaction record
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            WalletId = request.WalletId,
            Amount = request.Amount,
            Currency = CurrencyType.ETH,
            Type = TransactionType.Debit,
            Description = request.Description,
            SenderAddress = request.SenderAddress,
            ReceiverAddress = request.ReceiverAddress,
            Status = blockchainResponse.Mined && blockchainResponse.TransactionStatus
                ? TransactionStatus.Pending
                : TransactionStatus.Failed,
            TransactionHash = blockchainResponse.TransactionHash,
            BlockchainReference = blockchainResponse.TransactionHash,
            BlockNumber = blockchainResponse.BlockNumber,
            CreatedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _context.Transactions.Add(tx);
        await _context.SaveChangesAsync();

        // 6. Adjust balance if successful
        if (tx.Status == TransactionStatus.Pending)
        {
            await UpdateCurrencyBalance(request.WalletId, CurrencyType.ETH, -request.Amount);

            return new TransactionResult
            {
                Success = true,
                TransactionId = tx.Id,
                BlockchainReference = tx.TransactionHash,
                Message = "Transaction is pending confirmation"
            };
        }
        else
        {
            return new TransactionResult
            {
                Success = false,
                TransactionId = tx.Id,
                Message = blockchainResponse.ErrorMessage ?? "Transaction failed"
            };
        }
    }
    catch (RpcResponseException rpcEx)
    {
        _logger.LogError(rpcEx, "RPC error while sending ETH transaction");
        return new TransactionResult
        {
            Success = false,
            Message = $"RPC error: {rpcEx.Message}"
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error while sending ETH transaction");
        return new TransactionResult
        {
            Success = false,
            Message = $"Unexpected error: {ex.Message}"
        };
    }
}



        private async Task<BlockchainResponse> SubmitToEthereumNetworkAsync(
            string privateKey,
            TransactionRequest request)
        {
            try
            {
                var web3 = CreateWeb3(privateKey);


                // 3) Send the Ether transfer & wait for the receipt
                var transactionReceipt = await web3
                    .Eth
                    .GetEtherTransferService()
                    .TransferEtherAndWaitForReceiptAsync(
                        request.ReceiverAddress,
                        request.Amount);

                return new BlockchainResponse
                {
                    Mined              = true,
                    TransactionHash    = transactionReceipt.TransactionHash,
                    BlockNumber        = transactionReceipt.BlockNumber.ToString(),
                    GasUsed            = transactionReceipt.GasUsed.ToString(),
                    TransactionStatus  = transactionReceipt.Status.Value == 1
                };
            }
            catch (Exception ex)
            {
                return new BlockchainResponse
                {
                    Mined        = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        


        public async Task<int> UpdateTransactionConfirmationsAsync()
        {
            var pendingTransactions = await _context.Transactions
                .Where(t => t.Status == TransactionStatus.Pending && t.BlockNumber != null)
                .ToListAsync();

            int updatedCount = 0;

            if (pendingTransactions.Any())
            {
                var web3 = new Web3(_nodeUrl);
                var currentBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                var currentBlockNumber = (int)currentBlock.Value;

                foreach (var tx in pendingTransactions)
                {
                    if (int.TryParse(tx.BlockNumber, out int txBlockNumber))
                    {
                        int confirmations = currentBlockNumber - txBlockNumber + 1;
                        if (confirmations >= 12)
                        {
                            tx.Status    = TransactionStatus.Successful;
                            tx.UpdatedAt = DateTime.UtcNow;
                            updatedCount++;
                        }
                    }
                }

                if (updatedCount > 0)
                    await _context.SaveChangesAsync();
            }

            return updatedCount;
        }


        public async Task<UpdateTransactionModel> UpdateTransactionAsync(UpdateTransactionModel model)
        {
            var transaction = _mapper.Map<Models.Transaction>(model);
            _context.Transactions.Update(transaction);
            await _context.SaveChangesAsync();
            return _mapper.Map<UpdateTransactionModel>(transaction);
        }

        public async Task<DeleteTransactionModel> DeleteTransactionAsync(int transactionId)
        {
            var transaction = await _context.Transactions.FindAsync(transactionId);
            if (transaction == null)
                return null;
            _context.Transactions.Remove(transaction);
            await _context.SaveChangesAsync();
            return _mapper.Map<DeleteTransactionModel>(transaction);
        }

        public async Task<ViewTransactionModel?> ViewTransactionAsync(int transactionId)
        {
            var transaction = await _context.Transactions.FindAsync(transactionId);
            if (transaction == null)
                return null;
            return _mapper.Map<ViewTransactionModel>(transaction);
        }
        
        public async Task<IEnumerable<ViewTransactionModel>> SearchTransactionsAsync(
        Guid? walletId = null,
        DateTime? startDate = null, 
        DateTime? endDate = null,
        decimal? minAmount = null,
        decimal? maxAmount = null,
        string? transactionHash = null,
        TransactionStatus? status = null)
    {
        try 
        {
            var query = _context.Transactions.AsQueryable();
            
          
            if (walletId.HasValue)
            {
                query = query.Where(t => t.WalletId == walletId.Value);
            }
            
            if (startDate.HasValue)
            {
                query = query.Where(t => t.CreatedAt >= startDate.Value);
            }
            
            if (endDate.HasValue)
            {
                // Include the entire end date (up to 23:59:59)
                var adjustedEndDate = endDate.Value.Date.AddDays(1).AddSeconds(-1);
                query = query.Where(t => t.CreatedAt <= adjustedEndDate);
            }
            
            if (minAmount.HasValue)
            {
                query = query.Where(t => t.Amount >= minAmount.Value);
            }
            
            if (maxAmount.HasValue)
            {
                query = query.Where(t => t.Amount <= maxAmount.Value);
            }
            
            if (!string.IsNullOrWhiteSpace(transactionHash))
            {
                query = query.Where(t => t.TransactionHash.Contains(transactionHash) || 
                                         t.BlockchainReference.Contains(transactionHash));
            }
            
            if (status.HasValue)
            {
                query = query.Where(t => t.Status == status.Value);
            }
            
            // Order by most recent first
            query = query.OrderByDescending(t => t.CreatedAt);
            
            var transactions = await query.ToListAsync();
            return _mapper.Map<IEnumerable<ViewTransactionModel>>(transactions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching transactions for ${walletId}", walletId);
            throw;
        }
    }
        
        public async Task<IEnumerable<ViewTransactionModel>> GetTransactionsByUserIdAsync(string userId)
        {
            try
            {
                // First get all wallets for this user
                var userWallets = await _context.Wallets
                    .Where(w => w.UserId == userId)
                    .Select(w => w.Id)
                    .ToListAsync();

                if (!userWallets.Any())
                {
                    return Enumerable.Empty<ViewTransactionModel>();
                }

                // Get all transactions for these wallets
                var transactions = await _context.Transactions
                    .Where(t => userWallets.Contains(t.WalletId))
                    .OrderByDescending(t => t.CreatedAt)
                    .ToListAsync();

                return _mapper.Map<IEnumerable<ViewTransactionModel>>(transactions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting transactions for user {UserId}", userId);
                throw;
            }
        }
        
        
        public async Task<IEnumerable<ViewTransactionModel>> SearchTransactionsByUserIdAsync(
            string userId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            decimal? minAmount = null,
            decimal? maxAmount = null,
            string? transactionHash = null,
            TransactionStatus? status = null)
        {
            // 1. Get wallets
            var walletIds = await _context.Wallets
                .Where(w => w.UserId == userId)
                .Select(w => w.Id)
                .ToListAsync();

            // 2. Base query
            var query = _context.Transactions
                .Where(t => walletIds.Contains(t.WalletId))
                .AsQueryable();

            // 3. Apply same filters as before
            if (startDate.HasValue)
                query = query.Where(t => t.CreatedAt >= startDate.Value);

            if (endDate.HasValue)
            {
                var adjustedEnd = endDate.Value.Date.AddDays(1).AddSeconds(-1);
                query = query.Where(t => t.CreatedAt <= adjustedEnd);
            }

            if (minAmount.HasValue)
                query = query.Where(t => t.Amount >= minAmount.Value);

            if (maxAmount.HasValue)
                query = query.Where(t => t.Amount <= maxAmount.Value);

            if (!string.IsNullOrWhiteSpace(transactionHash))
                query = query.Where(t =>
                    t.TransactionHash.Contains(transactionHash) ||
                    t.BlockchainReference.Contains(transactionHash));

            if (status.HasValue)
                query = query.Where(t => t.Status == status.Value);

            // 4. Execute
            var results = await query
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return _mapper.Map<IEnumerable<ViewTransactionModel>>(results);
        }
        
        
        
        
        
        
        
        public async Task<TransactionResult> SendCurrencyAsync(CurrencyTransactionRequest request)
        {
            try
            {
                var wallet = await _context.Set<Wallet>().FirstOrDefaultAsync(w => w.Id == request.WalletId);
                if (wallet == null)
                {
                    throw new Exception("Wallet not found");
                }

                // Get currency configuration
                var currencyConfig = await _context.CurrencyConfigs
                    .FirstOrDefaultAsync(c => c.Currency == request.Currency && c.IsActive);
            
                if (currencyConfig == null)
                {
                    return new TransactionResult 
                    {
                        Success = false,
                        Message = $"Currency {request.Currency} is not supported"
                    };
                }

                // Check currency balance
                var currencyBalance = await GetCurrencyBalance(request.WalletId, request.Currency);
                _logger.LogInformation($"Currency Balance: {currencyBalance}");

                if (currencyBalance < request.Amount)
                {
                    return new TransactionResult 
                    {
                        Success = false,
                        Message = "Insufficient balance"
                    };
                }

                // Route to appropriate blockchain service based on currency
                return request.Currency switch
                {
                    CurrencyType.ETH => await SendEthereumAsync(request),
                    CurrencyType.BTC => await SendBitcoinAsync(request, currencyConfig),
                    CurrencyType.USDT => await SendERC20TokenAsync(request, currencyConfig),
                    CurrencyType.USDC => await SendERC20TokenAsync(request, currencyConfig),
                    _ => throw new NotSupportedException($"Currency {request.Currency} not yet implemented")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing {request.Currency} transaction: {ex.Message}");
                throw;
            }
        }
        
        private async Task<TransactionResult> SendBitcoinAsync(CurrencyTransactionRequest request, CurrencyConfig config)
        {
            // Implement Bitcoin transaction logic here
            // You would need to integrate with a Bitcoin library like NBitcoin
            throw new NotImplementedException("Bitcoin transactions not yet implemented");
        }

        // Placeholder for ERC-20 token transactions
        private async Task<TransactionResult> SendERC20TokenAsync(CurrencyTransactionRequest request, CurrencyConfig config)
        {
            // Implement ERC-20 token transfer logic here
            // Use Nethereum's ERC-20 functions
            throw new NotImplementedException("ERC-20 token transactions not yet implemented");
        }
        
        public async Task<decimal> GetCurrencyBalance(Guid walletId, CurrencyType currency)
        {
            var walletBalance = await _context.WalletBalances
                .FirstOrDefaultAsync(wb => wb.WalletId == walletId && wb.Currency == currency);
        
            return walletBalance?.Balance ?? 0;
        }

        private async Task UpdateCurrencyBalance(Guid walletId, CurrencyType currency, decimal amount)
        {
            var walletBalance = await _context.WalletBalances
                .FirstOrDefaultAsync(wb => wb.WalletId == walletId && wb.Currency == currency);

            if (walletBalance == null)
            {
                walletBalance = new WalletBalance
                {
                    WalletId = walletId,
                    Currency = currency,
                    Balance = 0,
                    CreatedAt = DateTime.UtcNow
                };
                _context.WalletBalances.Add(walletBalance);
            }

            walletBalance.Balance += amount;
            walletBalance.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }

        
        public async Task UpdateCurrencyBalancesBulkAsync(
            IEnumerable<(Guid WalletId, CurrencyType Currency, decimal Amount)> updates)
        {
            var walletIds = updates.Select(u => u.WalletId).Distinct().ToList();
            var currencies = updates.Select(u => u.Currency).Distinct().ToList();
    
            var existingBalances = await _context.WalletBalances
                .Where(wb => walletIds.Contains(wb.WalletId) && currencies.Contains(wb.Currency))
                .ToListAsync();
    
            var balanceDict = existingBalances
                .ToDictionary(wb => new { wb.WalletId, wb.Currency }, wb => wb);
    
            var newBalances = new List<WalletBalance>();
    
            foreach (var (walletId, currency, amount) in updates)
            {
                var key = new { WalletId = walletId, Currency = currency };
        
                if (balanceDict.TryGetValue(key, out var balance))
                {
                    balance.Balance += amount;
                    balance.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    newBalances.Add(new WalletBalance
                    {
                        WalletId = walletId,
                        Currency = currency,
                        Balance = amount,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }
    
            if (newBalances.Any())
                _context.WalletBalances.AddRange(newBalances);
    
            await _context.SaveChangesAsync();
        }
        
        public async Task<int> UpdateTransactionConfirmationsBatchAsync()
        {
            const int batchSize = 100;
            var totalUpdated = 0;
    
            while (true)
            {
                var batch = await _context.Transactions
                    .Where(t => t.Status == TransactionStatus.Pending && t.BlockNumber != null)
                    .Take(batchSize)
                    .ToListAsync();
            
                if (!batch.Any()) break;
        
                var web3 = new Web3(_nodeUrl);
                var currentBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                var currentBlockNumber = (int)currentBlock.Value;
        
                var updated = 0;
                foreach (var tx in batch)
                {
                    if (int.TryParse(tx.BlockNumber, out int txBlockNumber))
                    {
                        int confirmations = currentBlockNumber - txBlockNumber + 1;
                        if (confirmations >= 12)
                        {
                            tx.Status = TransactionStatus.Successful;
                            tx.UpdatedAt = DateTime.UtcNow;
                            updated++;
                        }
                    }
                }
        
                if (updated > 0)
                {
                    await _context.SaveChangesAsync();
                    totalUpdated += updated;
                }
        
                if (batch.Count < batchSize) break;
            }
    
            return totalUpdated;
        }
        
        private static readonly Func<WalletContext, Guid?, DateTime?, DateTime?, decimal?, decimal?, 
                string, TransactionStatus?, int, int, IAsyncEnumerable<Transaction>> 
            CompiledSearchQuery = EF.CompileAsyncQuery(
                (WalletContext context, Guid? walletId, DateTime? startDate, DateTime? endDate, 
                        decimal? minAmount, decimal? maxAmount, string transactionHash, 
                        TransactionStatus? status, int skip, int take) =>
                    context.Transactions
                        .Where(t => !walletId.HasValue || t.WalletId == walletId.Value)
                        .Where(t => !startDate.HasValue || t.CreatedAt >= startDate.Value)
                        .Where(t => !endDate.HasValue || t.CreatedAt <= endDate.Value.Date.AddDays(1).AddSeconds(-1))
                        .Where(t => !minAmount.HasValue || t.Amount >= minAmount.Value)
                        .Where(t => !maxAmount.HasValue || t.Amount <= maxAmount.Value)
                        .Where(t => string.IsNullOrEmpty(transactionHash) || 
                                    t.TransactionHash.Contains(transactionHash) ||
                                    t.BlockchainReference.Contains(transactionHash))
                        .Where(t => !status.HasValue || t.Status == status.Value)
                        .OrderByDescending(t => t.CreatedAt)
                        .Skip(skip)
                        .Take(take));

        
        private Web3 CreateWeb3(string privateKey)
        {
            var account =  new Nethereum.Web3.Accounts.Account(privateKey, 11155111); // Sepolia
            return new Web3(account, _nodeUrl);
        }

        
        
        
        
        
    }