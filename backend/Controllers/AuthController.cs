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

          // 2) At this point, user was created successfully, and we have a raw passphrase + address
          string rawPassphrase = registerResult.Passphrase;
          string onChainAddress = registerResult.Address;
          
          _logger.LogInformation(
               "New user registered. Passphrase = {Passphrase}, Address = {Address}",
               rawPassphrase,
               onChainAddress
          );

          return Ok(new ApiResponse<object>
          {
               Success = true,
               Message = "User registered successfully! Please back up your wallet passphrase.",
               Data    = new
               {
                    Passphrase = rawPassphrase,
                    Address    = onChainAddress
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
     
     }
     
