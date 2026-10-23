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
    internal const string RequesterAccount = @"DEMO\usera";

    internal const string RequesterSid = "S-1-5-21-1111111111-2222222222-3333333333-1001";

    internal const string TechnicianAccount = @"DEMO\usero";

    internal const string TechnicianSid = "S-1-5-21-1111111111-2222222222-3333333333-1002";

    internal const string BothAccount = @"DEMO\both";

    internal const string BothSid = "S-1-5-21-1111111111-2222222222-3333333333-1003";

    internal const string NeitherAccount = @"DEMO\neither";

    internal const string NeitherSid = "S-1-5-21-1111111111-2222222222-3333333333-1004";

    internal static InstitutionalActor Requester { get; } = new(RequesterSid, RequesterAccount);

    internal static InstitutionalActor Technician { get; } = new(TechnicianSid, TechnicianAccount);
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
            services.RemoveAll<IInstitutionalGroupVerifier>();
            services.AddSingleton<IInstitutionalGroupVerifier, TestGroupVerifier>();
            services.RemoveAll<IInstitutionalActorSource>();
            services.AddSingleton<IInstitutionalActorSource, TestActorSource>();
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

internal sealed class InstitutionalTestHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    internal new const string Scheme = "PosterInstitutionalTest";

    internal const string AccountHeader = "X-Poster-Test-Account";

    internal const string SidHeader = "X-Poster-Test-Sid";

    internal const string GroupsHeader = "X-Poster-Test-Groups";

    public InstitutionalTestHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AccountHeader, out var accountValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var account = accountValues.ToString();
        var sid = Request.Headers[SidHeader].ToString();
        var actor = InstitutionalActor.Create(sid, account);
        if (actor is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("The test identity is not a verifiable institutional identity."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, actor.AccountName),
            new(ClaimTypes.PrimarySid, actor.SecurityIdentifier)
        };
        foreach (var group in Request.Headers[GroupsHeader].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(InstitutionalTestClaims.Group, group));
        }

        var identity = new ClaimsIdentity(claims, Scheme, ClaimTypes.Name, ClaimTypes.Role);
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

internal sealed class TestActorSource : IInstitutionalActorSource
{
    public InstitutionalActor? FromPrincipal(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true
            || principal.Identity.AuthenticationType != InstitutionalTestHandler.Scheme)
        {
            return null;
        }

        return InstitutionalActor.Create(
            principal.FindFirst(ClaimTypes.PrimarySid)?.Value,
            principal.Identity.Name);
    }
}
