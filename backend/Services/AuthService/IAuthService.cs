using Microsoft.AspNetCore.Identity;
using WalletBackend.Models.DTOS;

namespace WalletBackend.Services.AuthService;

public interface IAuthService 
{
    Task<RegisterResultDto> RegisterUserAsync(RegisterModel model);

    
    Task<AuthResult> LoginAsync(LoginModel model);
    Task<UserDto> GetByIdAsync(string userId);
}