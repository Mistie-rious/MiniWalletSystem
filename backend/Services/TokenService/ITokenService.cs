using WalletBackend.Models;

namespace WalletBackend.Services.TokenService;

public interface ITokenService 
{
    Task<string> GenerateTokenAsync(ApplicationUser user);
}