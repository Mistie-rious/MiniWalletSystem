using System;
using System.Collections.Generic;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using WalletBackend.Models.Enums;

namespace WalletBackend.Services.Functions
{
    public static class TransactionFunctions 
    {
        public static TransactionStatus DetermineTransactionStatus(Transaction tx, HexBigInteger currentBlock)
        {
            // Calculate the number of confirmations.
            int confirmations = (int)(currentBlock.Value - tx.BlockNumber.Value);
            
            // Typically, 12 or more confirmations indicate a final transaction.
            return confirmations >= 12 
                ? TransactionStatus.Successful 
                : TransactionStatus.Pending;
        }

        public static TransactionType DetermineTransactionType(Transaction tx, List<string> yourWalletAddresses)
        {
            // Determine if the transaction is associated with your wallet addresses.
            bool isFromYourWallet = yourWalletAddresses.Contains(tx.From.ToLower());
            bool isToYourWallet = tx.To != null && yourWalletAddresses.Contains(tx.To.ToLower());
            
            if (isFromYourWallet && isToYourWallet)
                return TransactionType.Internal;
            else if (isFromYourWallet)
                return TransactionType.Debit;
            else if (isToYourWallet)
                return TransactionType.Credit;
            else
            {
                // Option 1: If this situation is unexpected, throw an exception.
                throw new InvalidOperationException("Transaction does not match any known type.");

                // Option 2: Alternatively, return a default value if you have one:
                // return TransactionType.Unknown;
            }
        }

        public static string GenerateTransactionDescription(Transaction tx, List<string> yourWalletAddresses)
        {
            // Determine if the transaction is sent from or to your wallet addresses.
            bool isFromYourWallet = yourWalletAddresses.Contains(tx.From.ToLower());
            bool isToYourWallet = tx.To != null && yourWalletAddresses.Contains(tx.To.ToLower());
            decimal ethAmount = Web3.Convert.FromWei(tx.Value);
            
            if (isFromYourWallet && isToYourWallet)
                return $"Internal transfer of {ethAmount} ETH";
            else if (isFromYourWallet)
                return $"Sent {ethAmount} ETH to {tx.To}";
            else if (isToYourWallet)
                return $"Received {ethAmount} ETH from {tx.From}";
            else
                return $"Transaction of {ethAmount} ETH";
        }
    }
}
