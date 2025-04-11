using WalletBackend.Models.DTOS.Transaction;
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
}