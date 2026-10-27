using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace PosterPrintRequest.Web;

public static class InstitutionalEntry
{
    public const string StaffHome = "/staff";

    public const string OperatorHome = "/technician";

    public static async Task<string?> DestinationAsync(IAuthorizationService authorization, ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if ((await authorization.AuthorizeAsync(user, PosterAccess.OperatorPolicy)).Succeeded)
        {
            return OperatorHome;
        }

        if ((await authorization.AuthorizeAsync(user, PosterAccess.UserPolicy)).Succeeded)
        {
            return StaffHome;
        }

        return null;
    }
}

public static class InstitutionalEntryApplicationBuilderExtensions
{
    public static IApplicationBuilder UseInstitutionalEntry(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (HttpMethods.IsGet(context.Request.Method)
                && (path == PathString.Empty || path == "/"))
            {
                var authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
                var destination = await InstitutionalEntry.DestinationAsync(authorization, context.User);
                if (destination is not null)
                {
                    context.Response.Redirect(context.Request.PathBase.Add(destination).ToString());
                    return;
                }
            }

            await next();
        });
    }
}
