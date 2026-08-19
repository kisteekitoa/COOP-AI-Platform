using COOPAI.API.DTOs.Auth;
using COOPAI.API.Services.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace COOPAI.API.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AuthController(
    ICoopAuthenticationService authenticationService,
    IAntiforgery antiforgery) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("csrf")]
    public ActionResult<AntiforgeryTokenDto> Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new AntiforgeryTokenDto(tokens.RequestToken ?? string.Empty));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthUserDto>> Login(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        var antiforgeryError = await ValidateAntiforgeryAsync();
        if (antiforgeryError is not null)
            return BadRequest(antiforgeryError);

        var user = await authenticationService.LoginAsync(
            request.UserName,
            request.Password,
            cancellationToken);
        return user is null
            ? Unauthorized(InvalidCredentials())
            : Ok(user);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var antiforgeryError = await ValidateAntiforgeryAsync();
        if (antiforgeryError is not null)
            return BadRequest(antiforgeryError);

        await authenticationService.LogoutAsync();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthUserDto>> Me(CancellationToken cancellationToken)
    {
        var user = await authenticationService.GetCurrentUserAsync(User, cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }

    private static AuthErrorDto InvalidCredentials() => new(
        "InvalidCredentials",
        "ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง");

    private async Task<AuthErrorDto?> ValidateAntiforgeryAsync()
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return new AuthErrorDto(
                "InvalidAntiforgeryToken",
                "คำขอหมดอายุหรือไม่ถูกต้อง กรุณาลองใหม่อีกครั้ง");
        }
    }
}
