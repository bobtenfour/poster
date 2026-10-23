using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace PosterPrintRequest.Web;

public static class PosterAccess
{
    public const string UserPolicy = "UserFrontend";

    public const string OperatorPolicy = "TechnicianOperations";

    public static IServiceCollection AddPosterAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
            .AddNegotiate();
        services.AddSingleton<IInstitutionalActorSource, WindowsActorSource>();
        services.AddSingleton<IInstitutionalGroupVerifier, WindowsGroupVerifier>();
        services.AddSingleton<IAuthorizationHandler, InstitutionalGroupHandler>();
        services.AddSingleton<IAuthorizationHandler, InstitutionalParticipantHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, InstitutionalAuthorizationResultHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(UserPolicy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new InstitutionalGroupRequirement(InstitutionalGroups.Requesters)));
            options.AddPolicy(OperatorPolicy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new InstitutionalGroupRequirement(InstitutionalGroups.Technicians)));
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new InstitutionalParticipantRequirement())
                .Build();
        });
        return services;
    }
}

public sealed class InstitutionalAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }

        if (!authorizeResult.Forbidden
            || context.Request.Path.StartsWithSegments("/Account/AccessDenied"))
        {
            await context.ChallengeAsync();
            return;
        }

        context.Response.Redirect("/Account/AccessDenied");
    }
}
