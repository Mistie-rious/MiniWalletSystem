using AutoMapper;
using WalletBackend.Data;
using WalletBackend.Models.DTOS.Transaction;

namespace WalletBackend.Services.TransactionService;

public class TransactionService : ITransactionService
{
    private readonly WalletContext _context;

    private readonly IMapper _mapper;
  

    public TransactionService(IConfiguration configuration, ILogger<TransactionService> logger, IMapper mapper, WalletContext context)
    {
        _context = context;
        _mapper = mapper;
  
    }

    public async Task<CreateTransactionModel> CreateTransactionAsync(CreateTransactionModel model)
    {
        var transaction = _mapper.Map<Models.Transaction>(model);
        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();
        
        return _mapper.Map<CreateTransactionModel>(transaction);
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