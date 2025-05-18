namespace WalletBackend.Models.DTOS.Transaction;

public class UnlockRequest
{
    public Guid   WalletId   { get; set; }
    public string Passphrase { get; set; }
}