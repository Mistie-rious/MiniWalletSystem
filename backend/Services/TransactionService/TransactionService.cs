using System.Globalization;
using System.Text;
using System.Text.Json;
using AutoMapper;
using CsvHelper;
using iText.IO.Font.Constants;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using Microsoft.EntityFrameworkCore;
using Nethereum.Web3;
using WalletBackend.Data;
using WalletBackend.Models;
using WalletBackend.Services.WalletService;
using WalletBackend.Models.DTOS.Transaction;
using WalletBackend.Models.Enums;
using WalletBackend.Models.Responses;

namespace WalletBackend.Services.TransactionService;

 public class TransactionService : ITransactionService
    {
        private readonly WalletContext _context;
        private readonly string _nodeUrl;
        private readonly IMapper _mapper;
        private readonly ILogger<TransactionService> _logger;
        private readonly IWalletService _walletService;
        private readonly IWalletUnlockService _walletUnlockService;

        public TransactionService(IConfiguration configuration, ILogger<TransactionService> logger, IMapper mapper, WalletContext context, IWalletService walletService, IWalletUnlockService walletUnlockService)
        {
            _context = context;
            _mapper = mapper;
            _walletService = walletService;
            _logger = logger;
            _nodeUrl = configuration["Ethereum:NodeUrl"];
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
            // Use your existing SendMoneyAsync logic here
            // Just adapt it to work with the new structure

            if (!_walletUnlockService.TryGetPrivateKey(request.WalletId, out var privateKey))
            {
                return new TransactionResult
                {
                    Success = false,
                    Message = "Wallet is locked. Please unlock your wallet first."
                };
            }

            // Create transaction with currency info
            var blockchainResponse = await SubmitToEthereumNetworkAsync(privateKey, request);

// 3) build the transaction only once
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


        private async Task<BlockchainResponse> SubmitToEthereumNetworkAsync(
            string privateKeyHex,
            TransactionRequest request)
        {
            try
            {
                // 1) Create an Account directly from the hex‐encoded private key
                //    (11155111 is the Sepolia chain ID; change if you’re on a different network)
                var account = new Nethereum.Web3.Accounts.Account(privateKeyHex, chainId: 11155111);

                // 2) Initialize Web3 using that account
                var web3 = new Nethereum.Web3.Web3(account, _nodeUrl);

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
        
        


        public async Task UpdateTransactionConfirmationsAsync()
        {
            var pendingTransactions = await _context.Transactions
                .Where(t => t.Status == TransactionStatus.Pending && t.BlockNumber != null)
                .ToListAsync();

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
                            tx.Status = TransactionStatus.Successful;
                            tx.UpdatedAt = DateTime.UtcNow;
                        }
                    }
                }

                await _context.SaveChangesAsync();
            }
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
        }
        
        
        
        
        
        
        
    }