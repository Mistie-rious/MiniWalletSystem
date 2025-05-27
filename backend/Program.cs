using System;
using System.Text;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using WalletBackend.Data;
using WalletBackend.Models;
using WalletBackend.Models.Structures;
using WalletBackend.Services;
using WalletBackend.Services.AuthService;
using WalletBackend.Services.ExportService;
using WalletBackend.Services.TokenService;
using WalletBackend.Services.TransactionService;
using WalletBackend.Services.WalletService;
using Scrutor;

var builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
{
    Env.Load(); // Only load .env when running locally
}


builder.Configuration.AddEnvironmentVariables();

// Load connection string from env
var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");

// Configure JwtSettings from appsettings or env
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("JwtSettings")
);

// Enable CORS for your React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy => policy.WithOrigins("http://localhost:3000", "http://localhost:3001" )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

// Configure EF Core
builder.Services.AddDbContext<WalletContext>(options =>
{
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.CommandTimeout(30);
        // Remove or comment out the retry configuration
        // sqlOptions.EnableRetryOnFailure(
        //     maxRetryCount: 3,
        //     maxRetryDelay: TimeSpan.FromSeconds(5),
        //     errorNumbersToAdd: null);
    });
}, ServiceLifetime.Scoped);

// Configure connection pool


// Add Identity for user management
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<WalletContext>()
    .AddDefaultTokenProviders();

// IMPORTANT: Force JWT to be the default auth scheme
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwt = builder.Configuration.GetSection("JwtSettings");
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt["Issuer"],
        ValidAudience = jwt["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwt["Key"]!))
    };
});

// Add other services
builder.Services.AddMemoryCache();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAuthorization();
builder.Services.AddAutoMapper(typeof(Program).Assembly);
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, NoOpEmailSender<ApplicationUser>>();

// Custom services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.Decorate<ITransactionService, CachedTransactionService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<IWalletUnlockService, WalletUnlockService>();
builder.Services.AddScoped<IExportService, ExportService>();
// builder.Services.AddSingleton<WebSocketTransactionMonitorService>();
// builder.Services.AddHostedService<PollingTransactionMonitorService>();
// builder.Services.AddHostedService(provider => provider.GetRequiredService<WebSocketTransactionMonitorService>());
builder.Services.AddHttpClient<WalletBalanceService>();
builder.Services.AddHostedService<WalletBalanceService>();
builder.Services.AddHostedService<TransactionConfirmationService>();

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseCors("AllowReactApp");
app.UseAuthentication();
app.UseAuthorization();

app.MapIdentityApi<ApplicationUser>();
app.MapControllers();

app.Run();
