using System.Text;
using Microsoft.AspNetCore.Mvc;
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
    private readonly string _nodeUrl;
  

    public TransactionController(ITransactionService transactionService, ILogger<TransactionController> logger, IWalletService walletService , IConfiguration configuration)
    {
        _transactionService = transactionService;
        _logger = logger;
        _walletService = walletService;
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
                string.IsNullOrEmpty(request.Passphrase) || 
                string.IsNullOrEmpty(request.TransactionData))
            {
                return BadRequest("Missing required parameters");
            }

            string signature = _walletService.SignTransaction(
                request.EncryptedKeyStore,
                request.Passphrase,
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
    public async Task<ActionResult<TransactionResult>> SendMoney([FromBody] TransactionRequest request)
    {
        try
        {
            if (
                string.IsNullOrEmpty(request.SenderAddress) || 
                string.IsNullOrEmpty(request.ReceiverAddress) ||
                request.Amount <= 0)
            {
                return BadRequest("Invalid transaction parameters");
            }

            var result = await _transactionService.SendMoneyAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing transaction");
            return StatusCode(500, "An error occurred while processing the transaction");
        }
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
    
    [HttpGet("balance")]
        public async Task<ActionResult<string>> GetBalance(string address)
        {
            try
            {
                var web3 = new Web3(_nodeUrl);
                var balance = await web3.Eth.GetBalance.SendRequestAsync(address);
                var balanceEth = Web3.Convert.FromWei(balance);
                return Ok($"Balance for address: {balanceEth} ETH");
            }
            catch(System.Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    
}
