using WalletBackend.Models.DTOS.Transaction;
using WalletBackend.Models.Enums;
using WalletBackend.Models.Responses;

namespace WalletBackend.Services.TransactionService;

using WalletBackend.Models;
public interface ITransactionService
{
    Task<CreateTransactionModel> CreateTransactionAsync(CreateTransactionModel model);
    Task<UpdateTransactionModel> UpdateTransactionAsync(UpdateTransactionModel model);
    Task<DeleteTransactionModel> DeleteTransactionAsync(int transactionId);
    Task<ViewTransactionModel?> ViewTransactionAsync(int transactionId);
    Task<TransactionResult> SendMoneyAsync(TransactionRequest request);
    Task UpdateTransactionConfirmationsAsync();

    Task<IEnumerable<ViewTransactionModel>> SearchTransactionsAsync(
        Guid? walletId = null,
        DateTime? startDate = null, 
        DateTime? endDate = null,
        decimal? minAmount = null,
        decimal? maxAmount = null,
        string? transactionHash = null,
        TransactionStatus? status = null);

   
}