using Microsoft.AspNetCore.Mvc;
using Nethereum.Web3;
using System.Threading.Tasks;

namespace WalletBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EthereumController : ControllerBase
    {
        // Hardcoded values; later consider using configuration for these.
        private readonly string _nodeUrl = "https://eth-sepolia.g.alchemy.com/v2/OPEVNKgEfgyM3w8EfPeRVzmnglTuYeEA";
        private readonly string _transactionHash = "0x7e0f9b9b7554fcd9581585e7c3abbff19d000c712db4eb5a424800c1f9452324";

        // GET: api/ethereum/blocknumber
        [HttpGet("blocknumber")]
        public async Task<ActionResult<object>> GetBlockNumber()
        {
            try
            {
                var web3 = new Web3(_nodeUrl);
                var latestBlockNumber = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
                return Ok(new { message = "Latest Block Number", blockNumber = latestBlockNumber.Value });
            }
            catch (System.Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // GET: api/ethereum/transaction
        [HttpGet("transaction")]
        public async Task<ActionResult<object>> GetTransactionDetails()
        {
            try
            {
                var web3 = new Web3(_nodeUrl);
                var transaction = await web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(_transactionHash);

                if (transaction != null)
                {
                    // Create a JSON response with the transaction details
                    var txDetails = new
                    {
                        From = transaction.From,
                        To = transaction.To,
                        ValueInEth = Web3.Convert.FromWei(transaction.Value),
                        Gas = transaction.Gas.Value,
                        GasPriceInGwei = Web3.Convert.FromWei(transaction.GasPrice),
                        Nonce = transaction.Nonce.Value,
                        BlockNumber = transaction.BlockNumber?.Value,
                        InputData = transaction.Input
                    };

                    return Ok(txDetails);
                }
                else
                {
                    return NotFound(new { message = "Transaction not found or still pending." });
                }
            }
            catch (System.Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
