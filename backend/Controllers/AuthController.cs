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


     public AuthController(IAuthService authService)
     {
          _authService = authService;
     }

     [HttpPost("register")]
     public async Task<IActionResult>Register([FromBody] RegisterModel model)
     {
          if (!ModelState.IsValid)
          {
               var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
               return BadRequest(new ApiResponse<object> { Success = false, Errors = errors });
          }

          var result = await _authService.RegisterUserAsync(model);

          if (result.Succeeded)
          {
               return Ok(new ApiResponse<object>{Success = true, Message = "User registered successfully!"});
          }

          else
          {
               var errors = result.Errors.Select(e => e.Description).ToList();
               return BadRequest(new ApiResponse<object> { Success = false, Errors = errors });
          }
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
     
