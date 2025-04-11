using System.Text.Json;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
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
        var wallet = await _context.Set<Wallet>()
            .FirstOrDefaultAsync(w => w.Id == request.WalletId);

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

      
        string signature = _walletService.SignTransaction(
            encryptedKeyStore,
            request.Passphrase,
            serializedData);

        
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

     
        var blockchainResponse = await SubmitToBlockchainNetworkAsync(
          request);

        if (blockchainResponse.Success)
        {
            transaction.Status = TransactionStatus.Successful;
            transaction.BlockchainReference = blockchainResponse.TransactionHash;
            transaction.BlockNumber = blockchainResponse.BlockNumber;
            transaction.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            
            return new TransactionResult
            {
                Success = true,
                TransactionId = transaction.Id,
                BlockchainReference = blockchainResponse.TransactionHash,
                Message = "Transaction completed successfully"
            };
        }
        else
        {
            transaction.Status = TransactionStatus.Failed;
            transaction.UpdatedAt = DateTime.UtcNow;
            transaction.Description += $" | Failure reason: {blockchainResponse.ErrorMessage}";
            await _context.SaveChangesAsync();
            
            return new TransactionResult
            {
                Success = false,
                TransactionId = transaction.Id,
                Message = $"Transaction submission failed: {blockchainResponse.ErrorMessage}"
            };
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, $"Error processing transaction: {ex.Message}");
        throw;
    }
}
    
    private async Task<BlockchainResponse> SubmitToBlockchainNetworkAsync(
        TransactionRequest request)
    {
        try
 
        {
            var wallet = await _context.Set<Wallet>()
                .FirstOrDefaultAsync(w => w.Id == request.WalletId);

            if (wallet == null)
            {
                throw new Exception("Wallet not found");
            }
            
            var encryptedKeyStore = wallet.EncryptedKeyStore;
           
            
           

         
            var account = Nethereum.Web3.Accounts.Account.LoadFromKeyStore(encryptedKeyStore, request.Passphrase, 11155111);
            var web3 = new Nethereum.Web3.Web3(account, _nodeUrl);

        
           
      
         
            var transaction = await web3.Eth.GetEtherTransferService()
                .TransferEtherAndWaitForReceiptAsync(request.ReceiverAddress,request.Amount);
        
       
            var blockNumber = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
        
            return new BlockchainResponse
            {
                Success = true,
                TransactionHash = transaction.TransactionHash,
                BlockNumber = blockNumber.ToString(),
                GasUsed = transaction.GasUsed.ToString()
            };
        }
        catch (Exception ex)
        {
            return new BlockchainResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
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
    
    
}