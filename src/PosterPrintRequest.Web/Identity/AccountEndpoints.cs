using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using PosterPrintRequest.Infrastructure.Identity;

namespace PosterPrintRequest.Web;

public static class AccountEndpoints
{
    internal const bool SessionOnlySignIn = false;

    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapPost("/Account/Logout", SignOutAsync).RequireAuthorization();
    }

    public static string Destination(ClaimsPrincipal principal, string? returnUrl)
    {
        var home = principal.IsInRole(PosterRoles.Operator) ? "/technician" : "/";
        if (!IsLocal(returnUrl))
        {
            return home;
        }

        var path = returnUrl!.Split('?', 2)[0];
        if (principal.IsInRole(PosterRoles.Operator)
            && path.StartsWith("/technician", StringComparison.OrdinalIgnoreCase))
        {
            return returnUrl;
        }

        if (principal.IsInRole(PosterRoles.User) && IsUserPath(path))
        {
            return returnUrl;
        }

        return home;
    }

    private static async Task<IResult> SignOutAsync(SignInManager<ApplicationUser> signIn)
    {
        await signIn.SignOutAsync();
        return Results.Redirect("/Account/Login");
    }

    private static bool IsUserPath(string path) =>
        path.Equals("/", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/help", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/request", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/request/", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
        {
            return false;
        }

        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\'))
        {
            return false;
        }

        return !url.Contains("..", StringComparison.Ordinal) && !url.Contains('\\');
    }
}
