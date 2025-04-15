using System.Text;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using WalletBackend.Data;
using WalletBackend.Models;
using WalletBackend.Services;
using WalletBackend.Services.AuthService;
using WalletBackend.Services.ExportService;
using WalletBackend.Services.TokenService;
using WalletBackend.Services.TransactionService;
using WalletBackend.Services.WalletService;

var builder = WebApplication.CreateBuilder(args);

Env.Load();


builder.Configuration.AddEnvironmentVariables();

var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING");



builder.Services.AddDbContext<WalletContext>(options => 
    options.UseSqlServer(connectionString));

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAuthorization();
builder.Services.AddAuthentication();
builder.Services.AddAutoMapper(typeof(Program).Assembly);
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<WalletContext>()
    .AddDefaultTokenProviders();
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, NoOpEmailSender<ApplicationUser>>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "https://localhost:7276",
            ValidAudience = "https://localhost:7276",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("your_super_secret_key"))
        };
    });
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddSingleton<TransactionMonitorService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TransactionMonitorService>());
builder.Services.AddHostedService<WalletBalanceService>();
builder.Services.AddHostedService<TransactionConfirmationService>();
var app = builder.Build();

app.MapIdentityApi<ApplicationUser>();

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers(); 

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(); 
}



app.Run();
