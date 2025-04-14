using System.Transactions;

namespace WalletBackend.Models.Responses;

public class BlockchainResponse
{
        public bool Success { get; set; }
        public string TransactionHash { get; set; }
        public bool Mined { get; set; }
        public bool TransactionStatus { get; set; }
        public string BlockNumber { get; set; }
        public string GasUsed { get; set; }
        public string ErrorMessage { get; set; }

}