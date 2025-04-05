using WalletBackend.Models.DTOS.Transaction;

namespace WalletBackend.Services.TransactionService;

using WalletBackend.Models;
public interface ITransactionService
{
    Task<CreateTransactionModel> CreateTransactionAsync(CreateTransactionModel model);
    Task<UpdateTransactionModel> UpdateTransactionAsync(UpdateTransactionModel model);
    Task<DeleteTransactionModel> DeleteTransactionAsync(int transactionId);
    Task<ViewTransactionModel?> ViewTransactionAsync(int transactionId);
}