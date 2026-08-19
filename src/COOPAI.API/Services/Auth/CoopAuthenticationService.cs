using System.Security.Claims;
using COOPAI.API.DTOs.Auth;
using COOPAI.API.Models.Auth;
using Microsoft.AspNetCore.Identity;

namespace COOPAI.API.Services.Auth;

public interface ICoopAuthenticationService
{
    Task<AuthUserDto?> LoginAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default);

    Task<AuthUserDto?> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);

    Task LogoutAsync();
}

public sealed class CoopAuthenticationService : ICoopAuthenticationService
{
    private readonly UserManager<CoopUser> _userManager;
    private readonly SignInManager<CoopUser> _signInManager;
    private readonly IPasswordHasher<CoopUser> _passwordHasher;
    private readonly CoopUser _dummyUser = new() { UserName = "authentication-probe" };
    private readonly string _dummyPasswordHash;

    public CoopAuthenticationService(
        UserManager<CoopUser> userManager,
        SignInManager<CoopUser> signInManager,
        IPasswordHasher<CoopUser> passwordHasher)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _passwordHasher = passwordHasher;
        _dummyPasswordHash = passwordHasher.HashPassword(_dummyUser, Guid.NewGuid().ToString("N"));
    }

    public async Task<AuthUserDto?> LoginAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedUserName = userName?.Trim() ?? string.Empty;
        var user = await _userManager.FindByNameAsync(normalizedUserName);
        if (user is null)
        {
            _passwordHasher.VerifyHashedPassword(_dummyUser, _dummyPasswordHash, password);
            return null;
        }

        var result = await _signInManager.CheckPasswordSignInAsync(
            user,
            password,
            lockoutOnFailure: true);
        if (!result.Succeeded || !user.IsEnabled)
            return null;

        await _signInManager.SignInAsync(user, isPersistent: false);
        return await MapAsync(user);
    }

    public async Task<AuthUserDto?> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.GetUserAsync(principal);
        return user is null || !user.IsEnabled ? null : await MapAsync(user);
    }

    public Task LogoutAsync() => _signInManager.SignOutAsync();

    private async Task<AuthUserDto> MapAsync(CoopUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return new AuthUserDto(
            user.Id,
            user.UserName ?? string.Empty,
            user.DisplayName,
            roles.OrderBy(role => role, StringComparer.Ordinal).ToArray());
    }
}
