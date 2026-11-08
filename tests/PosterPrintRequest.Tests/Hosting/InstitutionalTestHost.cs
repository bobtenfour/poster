using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Web;

namespace PosterPrintRequest.Tests.Hosting;

internal static class InstitutionalTestIdentities
{
    internal static readonly Guid RequesterOid = new("11111111-1111-1111-1111-000000000001");

    internal static readonly Guid TechnicianOid = new("11111111-1111-1111-1111-000000000002");

    internal static readonly Guid BothOid = new("11111111-1111-1111-1111-000000000003");

    internal static readonly Guid NeitherOid = new("11111111-1111-1111-1111-000000000004");

    internal const string RequesterAccount = "usera@test.example";

    internal const string RequesterSid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    internal const string TechnicianAccount = "usero@test.example";

    internal const string TechnicianSid = "S-1-5-21-1111111111-2222222222-3333333333-1002";

    internal const string BothAccount = "both@test.example";

    internal const string BothSid = "S-1-5-21-1111111111-2222222222-3333333333-1003";

    internal const string NeitherAccount = "neither@test.example";

    internal const string NeitherSid = "S-1-5-21-1111111111-2222222222-3333333333-1004";

    internal static InstitutionalActor Requester { get; } = new(RequesterOid, RequesterSid, RequesterAccount);

    internal static InstitutionalActor Technician { get; } = new(TechnicianOid, TechnicianSid, TechnicianAccount);
}

/// <summary>
/// Placeholder Entra identifiers for hosts under test. They name nothing real and no test contacts Entra.
/// </summary>
internal static class TestEntra
{
    internal const string TenantId = "00000000-0000-0000-0000-00000000aaaa";

    internal const string ClientId = "00000000-0000-0000-0000-00000000bbbb";

    internal static void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting(EntraSettings.TenantIdKey, TenantId);
        builder.UseSetting(EntraSettings.ClientIdKey, ClientId);
    }
}

internal static class InstitutionalTestClaims
{
    internal const string Group = "poster-test-group";
}

internal sealed class UnavailableActorSource : IInstitutionalActorSource
{
    public InstitutionalActor? FromPrincipal(ClaimsPrincipal principal) => null;
}

internal static class InstitutionalTestHost
{
    public static void UseTestAuthentication(IWebHostBuilder builder)
    {
        TestEntra.Configure(builder);
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = InstitutionalTestHandler.Scheme;
                options.DefaultAuthenticateScheme = InstitutionalTestHandler.Scheme;
                options.DefaultChallengeScheme = InstitutionalTestHandler.Scheme;
                options.DefaultForbidScheme = InstitutionalTestHandler.Scheme;
            });
            services.RemoveAll<IAuthenticationHandlerProvider>();
            services.AddScoped<IAuthenticationHandlerProvider, SkipNegotiateHandlerProvider>();
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, InstitutionalTestHandler>(InstitutionalTestHandler.Scheme, _ => { });
            if (services.Any(service => service.ServiceType == typeof(IInstitutionalGroupVerifier)))
            {
                // Only environments that keep the Windows group registration (for example Demo) have a verifier to replace.
                services.RemoveAll<IInstitutionalGroupVerifier>();
                services.AddSingleton<IInstitutionalGroupVerifier, TestGroupVerifier>();
            }
        });
    }
}

internal sealed class SkipNegotiateHandlerProvider : IAuthenticationHandlerProvider
{
    private readonly AuthenticationHandlerProvider _inner;

    public SkipNegotiateHandlerProvider(IAuthenticationSchemeProvider schemes)
    {
        _inner = new AuthenticationHandlerProvider(schemes);
    }

    public Task<IAuthenticationHandler?> GetHandlerAsync(HttpContext context, string authenticationScheme)
    {
        if (authenticationScheme == NegotiateDefaults.AuthenticationScheme)
        {
            return Task.FromResult<IAuthenticationHandler?>(new NoopNegotiateHandler());
        }

        return _inner.GetHandlerAsync(context, authenticationScheme);
    }

    private sealed class NoopNegotiateHandler : IAuthenticationRequestHandler
    {
        public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context) => Task.CompletedTask;

        public Task<AuthenticateResult> AuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(AuthenticationProperties? properties) => Task.CompletedTask;

        public Task<bool> HandleRequestAsync() => Task.FromResult(false);
    }
}

/// <summary>
/// Builds a principal from request headers, in the claim format of Authentication Contract sections 3 and 4.4:
/// oid, preferred_username, onprem_sid, and one roles claim per role, on an identity whose role claim type is roles.
/// </summary>
internal sealed class InstitutionalTestHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    internal new const string Scheme = "PosterInstitutionalTest";

    internal const string OidHeader = "X-Poster-Test-Oid";

    internal const string UsernameHeader = "X-Poster-Test-Username";

    internal const string SidHeader = "X-Poster-Test-Sid";

    internal const string RolesHeader = "X-Poster-Test-Roles";

    internal const string ExtraClaimsHeader = "X-Poster-Test-Extra";

    public InstitutionalTestHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(OidHeader, out var oidValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var actor = InstitutionalActor.Create(
            oidValues.ToString(),
            Request.Headers[SidHeader].ToString(),
            Request.Headers[UsernameHeader].ToString());
        if (actor is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("The test identity has no object identifier."));
        }

        var claims = new List<Claim>
        {
            new(PosterClaims.ObjectId, actor.ObjectId.ToString()),
            new(PosterClaims.Name, actor.PreferredUsername ?? "Test user")
        };
        if (actor.PreferredUsername is not null)
        {
            claims.Add(new Claim(PosterClaims.PreferredUsername, actor.PreferredUsername));
        }

        if (actor.OnPremisesSecurityIdentifier is not null)
        {
            claims.Add(new Claim(PosterClaims.OnPremisesSecurityIdentifier, actor.OnPremisesSecurityIdentifier));
        }

        foreach (var role in Request.Headers[RolesHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(PosterClaims.Roles, role));
            // Environments that keep the Windows group registration (Demo) read group membership instead of roles.
            claims.Add(new Claim(
                InstitutionalTestClaims.Group,
                role == PosterRoles.Requester ? InstitutionalGroups.Requesters : InstitutionalGroups.Technicians));
        }

        foreach (var extra in Request.Headers[ExtraClaimsHeader].ToString().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = extra.LastIndexOf('=');
            claims.Add(new Claim(extra[..separator], extra[(separator + 1)..]));
        }

        var identity = new ClaimsIdentity(claims, Scheme, PosterClaims.Name, PosterClaims.Roles);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}

internal sealed class TestGroupVerifier : IInstitutionalGroupVerifier
{
    public bool IsMember(ClaimsPrincipal principal, string groupName)
    {
        return principal.Identity?.IsAuthenticated == true
            && principal.Identity.AuthenticationType == InstitutionalTestHandler.Scheme
            && principal.HasClaim(InstitutionalTestClaims.Group, groupName);
    }
}
