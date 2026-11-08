using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace PosterPrintRequest.Web;

public static class PosterAccess
{
    public const string UserPolicy = "UserFrontend";

    public const string OperatorPolicy = "TechnicianOperations";

    /// <summary>
    /// Production and Development authenticate with Microsoft Entra ID (Authentication Contract section 5).
    /// Every other environment keeps its previous registration unchanged.
    /// </summary>
    public static bool UsesEntra(IHostEnvironment environment) =>
        environment.IsProduction() || environment.IsDevelopment();

    public static IServiceCollection AddPosterAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<IInstitutionalActorSource, ClaimsActorSource>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, InstitutionalAuthorizationResultHandler>();

        if (UsesEntra(environment))
        {
            AddEntra(services, configuration, environment);
        }
        else
        {
            AddWindowsGroups(services);
        }

        return services;
    }

    /// <summary>
    /// Builds the OpenIdConnect options now, so that a configuration refusal (Authentication Contract
    /// sections 5.14 and 6.7) stops startup. The client secret is read here, once.
    /// </summary>
    public static void RequireEntraConfiguration(this IServiceProvider services)
    {
        _ = services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(EntraSettings.ChallengeScheme);
    }

    private static void AddEntra(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = EntraSettings.CookieScheme;
                options.DefaultSignInScheme = EntraSettings.CookieScheme;
                options.DefaultChallengeScheme = EntraSettings.ChallengeScheme;
            })
            .AddCookie(EntraSettings.CookieScheme, options =>
            {
                options.ExpireTimeSpan = PosterSession.Lifetime;
                options.SlidingExpiration = false;
                options.Events.OnSigningIn = PosterSession.RecordExpiryAsync;
            })
            .AddOpenIdConnect(EntraSettings.ChallengeScheme, _ => { });
        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>>(new EntraOpenIdConnectConfiguration(configuration, environment));
        services.AddSingleton<IValidateOptions<OpenIdConnectOptions>>(new EntraOpenIdConnectValidation(environment));
        services.AddScoped<AuthenticationStateProvider, SessionAuthenticationStateProvider>();
        services.AddDataProtection().SetApplicationName("PosterPrintRequest");
        services.AddAuthorization(options =>
        {
            options.AddPolicy(UserPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(PosterRoles.Requester));
            options.AddPolicy(OperatorPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(PosterRoles.Technician));
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireRole(PosterRoles.Requester, PosterRoles.Technician)
                .Build();
        });
    }

    private static void AddWindowsGroups(IServiceCollection services)
    {
        services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
            .AddNegotiate();
        services.AddSingleton<IInstitutionalGroupVerifier, WindowsGroupVerifier>();
        services.AddSingleton<IAuthorizationHandler, InstitutionalGroupHandler>();
        services.AddSingleton<IAuthorizationHandler, InstitutionalParticipantHandler>();
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

        context.Response.Redirect(context.Request.PathBase.Add("/Account/AccessDenied").ToString());
    }
}
