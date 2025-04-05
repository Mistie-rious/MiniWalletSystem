using Microsoft.AspNetCore.Mvc;
using WalletBackend.Models.DTOS.Transaction;
using WalletBackend.Services.TransactionService;

namespace WalletBackend.Controllers;

[Route("/api/transactions")]
[ApiController]
public class TransactionController: ControllerBase
{
    private readonly ITransactionService _transactionService;

    public TransactionController(ITransactionService transactionService)
    {
        _transactionService = transactionService;
    }
    
    [HttpPost("create")]
    public async Task<IActionResult>Create([FromBody] CreateTransactionModel model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _transactionService.CreateTransactionAsync(model);

        if (result != null)
        {

            return Ok(result);
        }

       
        return BadRequest("Transaction could not be created.");
    }
}