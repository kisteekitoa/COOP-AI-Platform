using Microsoft.AspNetCore.Identity;

namespace COOPAI.API.Models.Auth;

public sealed class CoopUser : IdentityUser<int>
{
    public string DisplayName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
