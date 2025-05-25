using System;

namespace WalletBackend.Models.DTOS;

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? WalletAddress { get; set; }
    public Guid? WalletId { get; set; }
}
