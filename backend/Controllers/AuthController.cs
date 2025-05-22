using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WalletBackend.Models;
using WalletBackend.Models.DTOS;
using WalletBackend.Models.Responses;
using WalletBackend.Services.AuthService;

namespace WalletBackend.Controllers;

[Route("api/auth")]
[ApiController]
public class AuthController: ControllerBase
{
     private readonly IAuthService _authService;
     private readonly ILogger<AuthController> _logger;


     public AuthController(IAuthService authService, ILogger<AuthController> logger)
     {
          _authService = authService;
          _logger = logger;
     }

     [HttpPost("register")]
     public async Task<IActionResult> Register([FromBody] RegisterModel model)
     {
          if (!ModelState.IsValid)
          {
               var errors = ModelState
                    .Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

               return BadRequest(new ApiResponse<object>
               {
                    Success = false,
                    Errors  = errors
               });
          }

          // Call the updated service that returns RegisterResultDto
          var registerResult = await _authService.RegisterUserAsync(model);

          // 1) If IdentityResult failed, return the identity errors
          if (!registerResult.IdentityResult.Succeeded)
          {
               var idErrors = registerResult.IdentityResult.Errors
                    .Select(e => e.Description)
                    .ToList();

               return BadRequest(new ApiResponse<object>
               {
                    Success = false,
                    Errors  = idErrors
               });
          }

          // 2) At this point, user was created successfully, and we have a raw mnemonic + address
          string rawMnemonic    = registerResult.Mnemonic;
          string onChainAddress = registerResult.Address;
    
          _logger.LogInformation(
               "New user registered. Mnemonic = {Mnemonic}, Address = {Address}",
               rawMnemonic,
               onChainAddress
          );

          return Ok(new ApiResponse<object>
          {
               Success = true,
               Message = "User registered successfully! Please back up your wallet recovery mnemonic.",
               Data    = new
               {
                    Mnemonic = rawMnemonic,
                    Address  = onChainAddress
               }
          });
     }



     
     [HttpPost("login")]
     public async Task<IActionResult> Login([FromBody] LoginModel model)
     {
          if (!ModelState.IsValid)
          {
               var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
               return BadRequest(new ApiResponse<object> { Success = false, Errors = errors });
          }

          var authResult = await _authService.LoginAsync(model);

          if (authResult.Succeeded)
          {
               var loginResponse = new LoginResponse { Token = authResult.Token, userId = authResult.UserId };
               return Ok(new ApiResponse<LoginResponse>{Success = true, Data = loginResponse, Message = "Login successful!"});
          }

         else return Unauthorized(new ApiResponse<object>{Success = false, Message = "Invalid username or password!"});


          return Unauthorized(new { error = "Invalid login attempt." });
     }
     
     [HttpGet("{userId}")]
     public async Task<IActionResult> GetById(string userId)
     {
          try
          {
               var dto = await _authService.GetByIdAsync(userId);
               return Ok(new { success = true, data = dto });
          }
          catch (KeyNotFoundException)
          {
               return NotFound(new { success = false, message = "User not found" });
          }
          catch (Exception ex)
          {
               _logger.LogError(ex, "Error fetching user {UserId}", userId);
               return StatusCode(500, new { success = false, message = "Server error" });
          }
     }
     
     [HttpGet("me")]
     [Authorize]
     public async Task<IActionResult> Me()
     {
          try
          {
               // 1) extract user ID from JWT "sub" or NameIdentifier
               var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
               if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { success = false, message = "Invalid token" });

               // 2) reuse the same lookup
               var dto = await _authService.GetByIdAsync(userId);
               return Ok(new { success = true, data = dto });
          }
          catch (KeyNotFoundException)
          {
               return NotFound(new { success = false, message = "User not found" });
          }
          catch (Exception ex)
          {
               _logger.LogError(ex, "Error in /api/users/me");
               return StatusCode(500, new { success = false, message = "Server error" });
          }
     }
     
     [HttpPost("validate")]
     public async Task<IActionResult> ValidateToken([FromBody] TokenValidationRequest request)
     {
          if (string.IsNullOrEmpty(request?.Token))
          {
               return BadRequest(new { message = "Token is required." });
          }

          var userDto = await _authService.ValidateTokenAndGetUserAsync(request.Token);
          if (userDto != null)
          {
               return Ok(userDto);
          }

          return Unauthorized(new { message = "Invalid token" });
     }
     
     
     
     }
     
