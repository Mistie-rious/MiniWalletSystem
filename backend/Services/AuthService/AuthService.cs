using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using NBitcoin;
using WalletBackend.Data;
using WalletBackend.Models;
using WalletBackend.Models.DTOS;
using WalletBackend.Models.Enums;
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
    private readonly ILogger<AuthService> _logger;
    
    public AuthService(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, ITokenService tokenService, WalletContext context, IWalletService walletService, IMapper mapper, ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _context = context;
        _walletService = walletService;
        _mapper = mapper;
        _logger = logger;
    }
 
      public async Task<RegisterResultDto> RegisterUserAsync(RegisterModel model)
        {
            var resultDto = new RegisterResultDto
            {
                IdentityResult = IdentityResult.Success,
                Mnemonic       = null,
                Address        = null
            };

            // 1) Start a DB transaction
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 2) Check for existing email
                var existing = await _userManager.FindByEmailAsync(model.Email);
                if (existing != null)
                {
                    resultDto.IdentityResult = IdentityResult.Failed(
                        new IdentityError { Description = "Email is already in use." }
                    );
                    await transaction.RollbackAsync();
                    return resultDto;
                }

                // 3) Create Identity user
                var user   = _mapper.Map<ApplicationUser>(model);
                var identity = await _userManager.CreateAsync(user, model.Password);
                resultDto.IdentityResult = identity;

                if (!identity.Succeeded)
                {
                    await transaction.RollbackAsync();
                    return resultDto;
                }

                // 4) Use the user's login password to encrypt the keystore
                var (encryptedKeyStore, address, mnemonic) 
                    = _walletService.CreateNewWallet(model.Password);

                // 5) Persist the on‑chain wallet record
                var wallet = new Wallet
                {
                    UserId           = user.Id,
                    Address          = address,
                    EncryptedKeyStore= encryptedKeyStore,
                    Balance          = 0m,
                    CreatedAt        = DateTime.UtcNow
                };
                _context.Wallets.Add(wallet);
                await _context.SaveChangesAsync();
                
                var walletBalance = new WalletBalance
                {
                    WalletId = wallet.Id,
                    Balance  = 0m, // initial balance
                    Currency = CurrencyType.ETH, // or whatever default currency you want to set
                    CreatedAt = DateTime.UtcNow
                };
                _context.WalletBalances.Add(walletBalance);
                await _context.SaveChangesAsync();

                // 6) Link wallet to user (if you have navigation property)
                user.Wallet = wallet;
                await _userManager.UpdateAsync(user);

                // 7) Commit transaction
                await transaction.CommitAsync();

                // 8) Return mnemonic + address to caller
                resultDto.Mnemonic = mnemonic;
                resultDto.Address  = address;

                _logger.LogInformation(
                    "User {UserId} registered; wallet {Address} created",
                    user.Id, address
                );

                return resultDto;
            }
            catch (Exception ex)
            {
                // Roll back if possible
                if (_context.Database.CurrentTransaction != null)
                {
                    await _context.Database.CurrentTransaction.RollbackAsync();
                }

                _logger.LogError(ex, "Registration failed for {Email}", model.Email);

                // If Identity user was created, delete it
                if (resultDto.IdentityResult.Succeeded)
                {
                    var createdUser = await _userManager.FindByEmailAsync(model.Email);
                    if (createdUser != null)
                    {
                        await _userManager.DeleteAsync(createdUser);
                        _logger.LogInformation(
                            "Rolled back Identity user {UserId} after wallet error",
                            createdUser.Id
                        );
                    }
                }

                resultDto.IdentityResult = IdentityResult.Failed(
                    new IdentityError { Description = "An error occurred during registration." }
                );
                return resultDto;
            }
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
    
    
    public async Task<UserDto> GetByIdAsync(string userId)
    {
        var user = await _context.Users
            .Include(u => u.Wallet)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            throw new KeyNotFoundException($"User '{userId}' not found");

        return new UserDto
        {
            Id            = user.Id,
            Email         = user.Email,
            Username      = user.UserName,
            WalletId      = user.Wallet.Id,
            WalletAddress = user.Wallet.Address
        };
    }
    
    private static string GenerateRandomPassphrase(int length = 32)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()-_=+[]{}<>?";
        var random = new Random();
        var password = new char[length];

        for (int i = 0; i < length; i++)
        {
            password[i] = chars[random.Next(chars.Length)];
        }

        return new string(password);
    }

}
