using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace PosterPrintRequest.Web;

public static class AccountEndpoints
{
    public const string SignOutPath = "/Account/SignOut";

    /// <summary>
    /// Explicit sign-out (Authentication Contract section 5.11): a POST protected by anti-forgery validation that
    /// removes the application's authentication cookie and returns to the application entry. It does not call
    /// the Entra end-session endpoint.
    /// </summary>
    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapPost(SignOutPath, async (HttpContext context, IAntiforgery antiforgery) =>
            {
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.BadRequest();
                }

                await context.SignOutAsync(EntraSettings.CookieScheme);
                return Results.Redirect(context.Request.PathBase.Add("/").ToString());
            })
            .AllowAnonymous();
    }
}
