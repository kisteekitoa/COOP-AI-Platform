using COOPAI.API.Models.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace COOPAI.API.Security;

public sealed class CoopCookieAuthenticationEvents : CookieAuthenticationEvents
{
    private readonly ISecurityStampValidator _securityStampValidator;

    public CoopCookieAuthenticationEvents(ISecurityStampValidator securityStampValidator)
    {
        _securityStampValidator = securityStampValidator;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        // Compose the application-specific enabled-user check with Identity's
        // standard stamp validation so password changes and role refreshes work.
        await _securityStampValidator.ValidateAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated != true)
            return;

        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<CoopUser>>();
        var user = context.Principal is null
            ? null
            : await userManager.GetUserAsync(context.Principal);

        if (user is null || !user.IsEnabled)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
