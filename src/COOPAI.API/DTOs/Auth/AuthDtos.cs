using System.ComponentModel.DataAnnotations;

namespace COOPAI.API.DTOs.Auth;

public sealed class LoginRequestDto
{
    [Required]
    [StringLength(100)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [StringLength(256)]
    public string Password { get; set; } = string.Empty;
}

public sealed record AuthUserDto(
    int Id,
    string UserName,
    string DisplayName,
    IReadOnlyList<string> Roles);

public sealed record AuthErrorDto(string Code, string Message);

public sealed record AntiforgeryTokenDto(string RequestToken);
