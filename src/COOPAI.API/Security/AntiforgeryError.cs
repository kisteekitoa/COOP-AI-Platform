using Microsoft.AspNetCore.Antiforgery;

namespace COOPAI.API.Security;

public static class AntiforgeryError
{
    public static async Task<bool> IsInvalidAsync(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return false;
        }
        catch (AntiforgeryValidationException)
        {
            return true;
        }
    }
}
