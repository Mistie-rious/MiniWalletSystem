using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace WalletBackend.Services;

public class NoOpEmailSender<TUser> : IEmailSender<TUser> where TUser : class
{
    public Task SendConfirmationLinkAsync(TUser user, string email, string confirmationLink)
    {
     
        Console.WriteLine($"Confirmation link for {email}: {confirmationLink}");
        return Task.CompletedTask;
    }

    public Task SendPasswordResetLinkAsync(TUser user, string email, string resetLink)
    {
       
        Console.WriteLine($"Password reset link for {email}: {resetLink}");
        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(TUser user, string email, string resetCode)
    {
       
        Console.WriteLine($"Password reset code for {email}: {resetCode}");
        return Task.CompletedTask;
    }
}