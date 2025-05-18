using Microsoft.AspNetCore.Identity;

namespace WalletBackend.Models.DTOS;

public class RegisterResultDto
{
    public IdentityResult IdentityResult { get; set; }
    public string Mnemonic { get; set; }    // ← new
    public string Address { get; set; }
}