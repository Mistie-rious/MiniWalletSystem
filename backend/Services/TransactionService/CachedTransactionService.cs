using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using WalletBackend.Models.DTOS.Transaction;
using WalletBackend.Models.Enums;
using WalletBackend.Models.Responses;

namespace WalletBackend.Services.TransactionService;

// Add caching service
public class CachedTransactionService : ITransactionService
{
    private readonly ITransactionService _transactionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachedTransactionService> _logger;
    
    public CachedTransactionService(
        ITransactionService transactionService,
        IMemoryCache cache,
        ILogger<CachedTransactionService> logger)
    {
        _transactionService = transactionService;
        _cache = cache;
        _logger = logger;
    }
    
    public async Task<decimal> GetCurrencyBalance(Guid walletId, CurrencyType currency)
    {
        var cacheKey = $"balance:{walletId}:{currency}";
        
        if (_cache.TryGetValue(cacheKey, out decimal cachedBalance))
        {
            return cachedBalance;
        }
        
        var balance = await _transactionService.GetCurrencyBalance(walletId, currency);
        
        _cache.Set(cacheKey, balance, TimeSpan.FromMinutes(5));
        
        return balance;
    }
    
       public Task<CreateTransactionModel> CreateTransactionAsync(CreateTransactionModel model)
        => _transactionService.CreateTransactionAsync(model);

    public Task<UpdateTransactionModel> UpdateTransactionAsync(UpdateTransactionModel model)
        => _transactionService.UpdateTransactionAsync(model);

    public Task<DeleteTransactionModel> DeleteTransactionAsync(int transactionId)
        => _transactionService.DeleteTransactionAsync(transactionId);

    public Task<ViewTransactionModel?> ViewTransactionAsync(int transactionId)
        => _transactionService.ViewTransactionAsync(transactionId);

    public Task<IEnumerable<ViewTransactionModel>> SearchTransactionsAsync(Guid? walletId = null, DateTime? startDate = null,
        DateTime? endDate = null, decimal? minAmount = null, decimal? maxAmount = null, string? transactionHash = null,
        TransactionStatus? status = null)
        => _transactionService.SearchTransactionsAsync(walletId, startDate, endDate, minAmount, maxAmount, transactionHash, status);

    public Task<TransactionResult> SendCurrencyAsync(CurrencyTransactionRequest request)
        => _transactionService.SendCurrencyAsync(request);

    public Task<TransactionResult> SendEthereumAsync(CurrencyTransactionRequest request)
        => _transactionService.SendEthereumAsync(request);

    public Task<IEnumerable<ViewTransactionModel>> GetTransactionsByUserIdAsync(string userId)
        => _transactionService.GetTransactionsByUserIdAsync(userId);

    public Task<IEnumerable<ViewTransactionModel>> SearchTransactionsByUserIdAsync(string userId, DateTime? startDate = null,
        DateTime? endDate = null, decimal? minAmount = null, decimal? maxAmount = null, string? transactionHash = null,
        TransactionStatus? status = null)
        => _transactionService.SearchTransactionsByUserIdAsync(userId, startDate, endDate, minAmount, maxAmount, transactionHash, status);

    public Task<int> UpdateTransactionConfirmationsAsync()
        => _transactionService.UpdateTransactionConfirmationsAsync();

    public Task UpdateCurrencyBalancesBulkAsync(IEnumerable<(Guid WalletId, CurrencyType Currency, decimal Amount)> updates)
        => _transactionService.UpdateCurrencyBalancesBulkAsync(updates);

    public Task<int> UpdateTransactionConfirmationsBatchAsync()
        => _transactionService.UpdateTransactionConfirmationsBatchAsync();
}