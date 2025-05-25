using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nethereum.Web3;
using WalletBackend.Models.DTOS.Transaction;
using WalletBackend.Models.Enums;
using WalletBackend.Models.Responses;
using WalletBackend.Services.TransactionService;
using WalletBackend.Services.WalletService;

namespace WalletBackend.Controllers;

[Route("/api/transactions")]
[ApiController]
public class TransactionController: ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly ILogger<TransactionController> _logger;
    private readonly IWalletService _walletService;
    private readonly IWalletUnlockService _walletUnlockService;
    
    private readonly string _nodeUrl;
  

    public TransactionController(ITransactionService transactionService, ILogger<TransactionController> logger, IWalletService walletService , IConfiguration configuration, IWalletUnlockService walletUnlockService)
    {
        _transactionService = transactionService;
        _logger = logger;
        _walletService = walletService;
        _walletUnlockService = walletUnlockService;
        _nodeUrl = configuration["Ethereum:NodeUrl"];

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
    
    [HttpPost("sign")]
    public ActionResult<string> SignTransaction([FromBody] SignTransactionRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.EncryptedKeyStore) || 
                string.IsNullOrEmpty(request.password) || 
                string.IsNullOrEmpty(request.TransactionData))
            {
                return BadRequest("Missing required parameters");
            }

            string signature = _walletService.SignTransaction(
                request.EncryptedKeyStore,
                request.password,
                request.TransactionData);

            return Ok(new { Signature = signature });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error signing transaction");
            return StatusCode(500, "An error occurred while signing the transaction");
        }
    }

    
    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] CurrencyTransactionRequest request)
    {
        var result = await _transactionService.SendEthereumAsync(request);
        if (!result.Success) 
            return BadRequest(result.Message);
        return Ok(new {
            result.TransactionId,
            result.BlockchainReference,
            result.Message
        });
    }

    
    [HttpGet("search")]
    public async Task<IActionResult> SearchTransactions(
        [FromQuery] Guid? walletId,
        [FromQuery] DateTime? startDate, 
        [FromQuery] DateTime? endDate,
        [FromQuery] decimal? minAmount,
        [FromQuery] decimal? maxAmount,
        [FromQuery] string? transactionHash,
        [FromQuery] TransactionStatus? status)
    {
        try
        {
            var transactions = await _transactionService.SearchTransactionsAsync(
                walletId, startDate, endDate, minAmount, maxAmount, transactionHash, status);
            
            return Ok(transactions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching transactions");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
    
    // [HttpGet("balance")]
    //     public async Task<ActionResult<string>> GetBalance(string address)
    //     {
    //         try
    //         {
    //             var web3 = new Web3(_nodeUrl);
    //             var balance = await web3.Eth.GetBalance.SendRequestAsync(address);
    //             var balanceEth = Web3.Convert.FromWei(balance);
    //             return Ok($"Balance for address: {balanceEth} ETH");
    //         }
    //         catch(System.Exception ex)
    //         {
    //             return BadRequest(new { message = ex.Message });
    //         }
    //     }
        
        
    [HttpPost("unlock")]
    public async Task<IActionResult> Unlock([FromBody] UnlockRequest req)
    {
        try
        {
            await _walletUnlockService.UnlockAsync(req.WalletId, req.Password);
            return Ok("Wallet unlocked for 30 minutes");
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Wallet not found");
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest("Invalid password");
        }
    }
    
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetByUserId(string userId)
    {
        var txs = await _transactionService.GetTransactionsByUserIdAsync(userId);
        return Ok(txs);
    }

    /// <summary>
    /// GET /api/transactions/user/{userId}/search
    /// supports same filters as /search, scoped to that user.
    /// </summary>
    [HttpGet("user/{userId}/search")]
    public async Task<IActionResult> SearchByUserId(
        string userId,
        [FromQuery] DateTime? startDate, 
        [FromQuery] DateTime? endDate,
        [FromQuery] decimal? minAmount,
        [FromQuery] decimal? maxAmount,
        [FromQuery] string? transactionHash,
        [FromQuery] TransactionStatus? status)
    {
        var txs = await _transactionService.SearchTransactionsByUserIdAsync(
            userId, startDate, endDate, minAmount, maxAmount, transactionHash, status);
        return Ok(txs);
    }
    
    [HttpGet("balance")]
    public async Task<IActionResult> GetWalletBalance()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var balance = await _walletService.GetBalanceByUserIdAsync(userId);
            return Ok(new
            {
                success = true,
                balance
            });
        }
        catch (Exception ex)
        {
            return NotFound(new { success = false, message = ex.Message });
        }
    }
    
}
