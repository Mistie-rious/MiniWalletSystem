using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using WalletBackend.Data;
using WalletBackend.Models;
using WalletBackend.Models.DTOS;
using WalletBackend.Services.TokenService;
using WalletBackend.Services.WalletService;

namespace WalletBackend.Services.AuthService;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly WalletContext _context;
    private readonly IWalletService _walletService;
    private readonly IMapper _mapper;
    
    public AuthService(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, ITokenService tokenService, WalletContext context, IWalletService walletService, IMapper mapper)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _context = context;
        _walletService = walletService;
        _mapper = mapper;
    }

    public async Task<IdentityResult> RegisterUserAsync(RegisterModel model)
    {
        var existingUser = await _userManager.FindByEmailAsync(model.Email);
        if (existingUser != null)
        {
            return IdentityResult.Failed();
        }

   
        var user = _mapper.Map<ApplicationUser>(model);
        var result = await _userManager.CreateAsync(user, model.Password);
    
        var (privateKey, address) = _walletService.CreateNewWallet();
        if (result.Succeeded)
        {
            var wallet = new Wallet
            {
             
                Balance = 0,
                UserId = user.Id,
                Address = address,
            };
            
            _context.Wallets.Add(wallet);
            await _context.SaveChangesAsync();
        
            user.Wallet = wallet;
            await _userManager.UpdateAsync(user);
        }
    
        return result;
    }

    public async Task<AuthResult> LoginAsync(LoginModel model)
    {
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            return new AuthResult
            {
                Succeeded = false
            };
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);
        
        if (result.Succeeded)
        {
            var token = await _tokenService.GenerateTokenAsync(user);
            return new AuthResult
            {
                Succeeded = true,
                UserId = user.Id,
                Token = token,
                SignInResult = result
            };
        }
        
        return new AuthResult
        {
            Succeeded = false,
            SignInResult = result
        };
    }
}
