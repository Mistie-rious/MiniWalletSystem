using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WalletBackend.Models.Enums;

namespace WalletBackend.Models;


public class ApplicationUser : IdentityUser
{
    
    [Required]

    public Wallet Wallet { get; set; } 


    
    [Required]
    public UserRole Role { get; set; } = UserRole.User;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; } 
    

}