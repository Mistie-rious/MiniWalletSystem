using Microsoft.AspNetCore.Identity;

namespace WalletBackend.Services.AuthService;

public class AuthResult
{
    
        public bool Succeeded { get; set; }
        public string UserId { get; set; }
        public string Token { get; set; }
        public SignInResult SignInResult { get; set; }
        
    
        // Helper properties to maintain readability in controller
        public bool IsLockedOut => SignInResult?.IsLockedOut ?? false;
        public bool IsNotAllowed => SignInResult?.IsNotAllowed ?? false;
        public bool RequiresTwoFactor => SignInResult?.RequiresTwoFactor ?? false;
}
