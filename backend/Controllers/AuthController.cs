using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WalletBackend.Models;
using WalletBackend.Models.DTOS;
using WalletBackend.Services.AuthService;

namespace WalletBackend.Controllers;

[Route("api/auth")]
[ApiController]
public class AuthController: ControllerBase
{
     private readonly IAuthService _authService;


     public AuthController(IAuthService authService)
     {
          _authService = authService;
     }

     [HttpPost("register")]
     public async Task<IActionResult>Register([FromBody] RegisterModel model)
     {
          if (!ModelState.IsValid)
               return BadRequest(ModelState);

          var result = await _authService.RegisterUserAsync(model);

          if (result.Succeeded)
          {
               return Ok(new {message = "User registered successfully!"});
          }

          foreach (var error in result.Errors)
               
          {
               return BadRequest(new { message = error.Description });
          }

          return BadRequest(ModelState);
     }

     
     [HttpPost("login")]
     public async Task<IActionResult> Login([FromBody] LoginModel model)
     {
          if (!ModelState.IsValid)
          {
               return BadRequest(new { 
                    errors = ModelState.Values
                         .SelectMany(v => v.Errors)
                         .Select(e => e.ErrorMessage) 
               });
          }

          var authResult = await _authService.LoginAsync(model);

          if (authResult.Succeeded)
          {
               return Ok(new { 
                    token = authResult.Token,
                    userId = authResult.UserId,
                    message = "Login successful" 
               });
          }

          if (authResult.IsLockedOut)
          {
               return Unauthorized(new { error = "Account is temporarily locked. Try again later." });
          }

          if (authResult.IsNotAllowed)
          {
               return Unauthorized(new { error = "Account is not confirmed. Check your email." });
          }

          if (authResult.RequiresTwoFactor)
          {
               return Ok(new { 
                    requiresAdditionalAction = true,
                    twoFactorRequired = true
               });
          }

          // Invalid credentials - keep message vague for security
          return Unauthorized(new { error = "Invalid login attempt." });
     }
     
     }
     
