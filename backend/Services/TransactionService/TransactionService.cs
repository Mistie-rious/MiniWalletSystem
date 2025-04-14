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

        public TransactionService(IConfiguration configuration, ILogger<TransactionService> logger, IMapper mapper, WalletContext context, IWalletService walletService)
        {
            _context = context;
            _mapper = mapper;
            _walletService = walletService;
            _logger = logger;
            _nodeUrl = configuration["Ethereum:NodeUrl"];
        }

        public async Task<CreateTransactionModel> CreateTransactionAsync(CreateTransactionModel model)
        {
            var transaction = _mapper.Map<Models.Transaction>(model);
            _context.Transactions.Add(transaction);
            await _context.SaveChangesAsync();
            return _mapper.Map<CreateTransactionModel>(transaction);
        }

        public async Task<TransactionResult> SendMoneyAsync(TransactionRequest request)
        {
            try
            {
                var wallet = await _context.Set<Wallet>().FirstOrDefaultAsync(w => w.Id == request.WalletId);
                if (wallet == null)
                {
                    throw new Exception("Wallet not found");
                }

                var encryptedKeyStore = wallet.EncryptedKeyStore;
                if (string.IsNullOrEmpty(encryptedKeyStore))
                {
                    throw new Exception("Encrypted keystore is missing in this wallet");
                }

                var transactionData = new
                {
                    From = request.SenderAddress,
                    To = request.ReceiverAddress,
                    Amount = request.Amount,
                    Timestamp = DateTime.UtcNow,
                    Nonce = Guid.NewGuid().ToString()
                };
                string serializedData = JsonSerializer.Serialize(transactionData);
                string signature = _walletService.SignTransaction(encryptedKeyStore, request.Passphrase, serializedData);

                var transaction = new Transaction
                {
                    TransactionHash = signature,
                    SenderAddress = request.SenderAddress,
                    ReceiverAddress = request.ReceiverAddress,
                    Status = TransactionStatus.Pending,
                    Type = TransactionType.Debit,
                    CreatedAt = DateTime.UtcNow,
                    Amount = request.Amount,
                    WalletId = request.WalletId,
                    Description = request.Description
                };

                _context.Transactions.Add(transaction);
                await _context.SaveChangesAsync();

                var blockchainResponse = await SubmitToBlockchainNetworkAsync(request);

                if (blockchainResponse.Mined)
                {
                    if (blockchainResponse.TransactionStatus == true)
                    {
                        transaction.Status = TransactionStatus.Pending;
                        transaction.BlockchainReference = blockchainResponse.TransactionHash;
                        transaction.BlockNumber = blockchainResponse.BlockNumber;
                    }
                    else
                    {
                        transaction.Status = TransactionStatus.Failed;
                        transaction.Description += " | Transaction failed on the blockchain";
                    }
                }
                else
                {
                    transaction.Status = TransactionStatus.Failed;
                    transaction.Description += $" | Submission failed: {blockchainResponse.ErrorMessage}";
                }

                transaction.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                if (blockchainResponse.Mined && blockchainResponse.TransactionStatus == true)
                {
                    return new TransactionResult
                    {
                        Success = true,
                        TransactionId = transaction.Id,
                        BlockchainReference = blockchainResponse.TransactionHash,
                        Message = "Transaction is pending confirmation"
                    };
                }
                else
                {
                    return new TransactionResult
                    {
                        Success = false,
                        TransactionId = transaction.Id,
                        Message = transaction.Description
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing transaction: {ex.Message}");
                throw;
            }
        }

        private async Task<BlockchainResponse> SubmitToBlockchainNetworkAsync(TransactionRequest request)
        {
            try
            {
                var wallet = await _context.Set<Wallet>().FirstOrDefaultAsync(w => w.Id == request.WalletId);
                if (wallet == null)
                {
                    throw new Exception("Wallet not found");
                }

                var encryptedKeyStore = wallet.EncryptedKeyStore;
                var account = Nethereum.Web3.Accounts.Account.LoadFromKeyStore(encryptedKeyStore, request.Passphrase, 11155111);
                var web3 = new Web3(account, _nodeUrl);

                var transactionReceipt = await web3.Eth.GetEtherTransferService()
                    .TransferEtherAndWaitForReceiptAsync(request.ReceiverAddress, request.Amount);

                return new BlockchainResponse
                {
                    Mined = true,
                    TransactionHash = transactionReceipt.TransactionHash,
                    BlockNumber = transactionReceipt.BlockNumber.ToString(),
                    GasUsed = transactionReceipt.GasUsed.ToString(),
                    TransactionStatus = transactionReceipt.Status.Value == 1
                };
            }
            catch (Exception ex)
            {
                return new BlockchainResponse
                {
                    Mined = false,
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
        
        
        
        
    }