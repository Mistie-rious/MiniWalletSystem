using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using NBitcoin;
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
        Passphrase = null,
        Address = null
    };

    // Start a database transaction to ensure consistency
    using var transaction = await _context.Database.BeginTransactionAsync();
    
    try
    {
        // --- 1) Check if email already exists ---
        var existing = await _userManager.FindByEmailAsync(model.Email);
        if (existing != null)
        {
            resultDto.IdentityResult = IdentityResult.Failed(
                new IdentityError { Description = "Email is already in use." }
            );
            await transaction.RollbackAsync();
            return resultDto;
        }

        // --- 2) Create the ApplicationUser (with ASP.NET Identity) ---
        var user = _mapper.Map<ApplicationUser>(model);
        var identity = await _userManager.CreateAsync(user, model.Password);
        resultDto.IdentityResult = identity;

        // If Identity failed (e.g. password too weak), return immediately
        if (!identity.Succeeded)
        {
            await transaction.RollbackAsync();
            return resultDto;
        }

        // --- 3) Identity succeeded: generate a random passphrase ---
        string passphrase = GenerateRandomPassphrase();

        // --- 4) Create the on-chain wallet using that passphrase ---
        var (encryptedKeyStore, address, mnemonic) = _walletService.CreateNewWallet(passphrase);

        // --- 5) Persist the new Wallet record, linked to user.Id ---
        var wallet = new Wallet
        {
            UserId = user.Id,
            Address = address,
            EncryptedKeyStore = encryptedKeyStore,
            Balance = 0m,
            CreatedAt = DateTime.UtcNow,
        };

        _context.Wallets.Add(wallet);
        await _context.SaveChangesAsync();

        // --- 6) Link back to ApplicationUser (if you keep a navigation property) ---
        user.Wallet = wallet;
        await _userManager.UpdateAsync(user);

        // Commit the transaction
        await transaction.CommitAsync();

        // --- 7) Populate the DTO so the controller can return the passphrase+address ---
        resultDto.Passphrase = passphrase;
        resultDto.Address = address;

        // Log successful registration (without sensitive data)
        _logger.LogInformation("User {UserId} successfully registered with wallet address {Address}", 
            user.Id, address);

        return resultDto;
    }
    catch (Exception ex)
    {
        // Check if transaction is still active before attempting rollback
        if (transaction.GetDbTransaction().Connection != null)
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Failed to rollback transaction during error handling");
            }
        }

        // Log the error
        _logger.LogError(ex, "Error occurred during user registration for email {Email}", model.Email);

        // Clean up the Identity user if it was created
        if (resultDto.IdentityResult.Succeeded)
        {
            try
            {
                var userToDelete = await _userManager.FindByEmailAsync(model.Email);
                if (userToDelete != null)
                {
                    await _userManager.DeleteAsync(userToDelete);
                    _logger.LogInformation("Cleaned up Identity user {UserId} after wallet creation failure", userToDelete.Id);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "Failed to clean up Identity user after wallet creation failure");
            }
        }

        // Return a failure result
        resultDto.IdentityResult = IdentityResult.Failed(
            new IdentityError { Description = "An error occurred during registration. Please try again." }
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
    
    private static string GenerateRandomPassphrase()
    {
        // This creates a new 24‑word English BIP‑39 mnemonic.
        var mnemonic = new Mnemonic(Wordlist.English, WordCount.TwentyFour);
        return mnemonic.ToString(); 
        // e.g. "abandon ability able about above absent absorb abstract absurd abuse access accident activity actor adapt adjust adult advance aunt agree ahead alarm"
    }
}
