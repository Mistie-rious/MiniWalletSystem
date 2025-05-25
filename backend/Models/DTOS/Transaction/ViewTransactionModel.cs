using System;
using System.ComponentModel.DataAnnotations;
using WalletBackend.Models.Enums;

namespace WalletBackend.Models.DTOS.Transaction;

public class ViewTransactionModel
{
    public Guid Id { get; set; }

    [Required]
    public Guid WalletId { get; set; }

    [Required]
    public CurrencyType Currency { get; set; }

    public string? TransactionHash { get; set; }

    public string? SenderAddress { get; set; }

    public string? ReceiverAddress { get; set; }

    [Required]
    public TransactionStatus Status { get; set; }

    [Required]
    public TransactionType Type { get; set; }

    [Required]
    public decimal Amount { get; set; }

    [Required]
    public DateTime? Timestamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string? Description { get; set; }

    public string? BlockchainReference { get; set; }

    public string? BlockNumber { get; set; }

    public string? Network { get; set; }
}
