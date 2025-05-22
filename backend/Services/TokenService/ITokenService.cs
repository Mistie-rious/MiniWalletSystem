using Microsoft.IdentityModel.Tokens;
using WalletBackend.Models;

namespace WalletBackend.Services.TokenService;

public interface ITokenService 
{
    Task<string> GenerateTokenAsync(ApplicationUser user);
    TokenValidationParameters GetTokenValidationParameters();
}