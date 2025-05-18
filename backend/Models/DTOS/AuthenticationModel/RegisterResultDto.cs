using Microsoft.AspNetCore.Identity;

namespace WalletBackend.Models.DTOS;

public class RegisterResultDto
{
    public IdentityResult IdentityResult { get; set; }
    public string Passphrase { get; set; }    // the newly generated passphrase
    public string Address { get; set; }       // (optional) return wallet address
}