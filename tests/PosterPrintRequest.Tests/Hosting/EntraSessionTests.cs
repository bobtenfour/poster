using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Web;
using Bunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Tests.Workflow;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Components.Foundation;

namespace PosterPrintRequest.Tests.Hosting;

/// <summary>
/// The application in the Development environment with its real cookie and OpenIdConnect registrations, a clock the
/// test moves, and a sign-in endpoint that issues the application cookie exactly as the OIDC callback would.
/// </summary>
internal sealed class SessionFactory : WebApplicationFactory<Program>
{
    internal const string SignInPath = "/__test/signin";

    private readonly string _connectionString;
    private readonly bool _virtualDirectory;
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "poster-session-" + Guid.NewGuid().ToString("N"));

    public SessionFactory(string connectionString, bool virtualDirectory = false)
    {
        _connectionString = connectionString;
        _virtualDirectory = virtualDirectory;
    }

    public ManualClock Clock { get; } = new();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        TestEntra.Configure(builder);
        Directory.CreateDirectory(_storageRoot);
        builder.UseSetting("SharedStorage:RootPath", _storageRoot);
        builder.UseSetting("ConnectionStrings:" + PersistenceServiceCollectionExtensions.ConnectionStringName, _connectionString);
        builder.ConfigureTestServices(services =>
        {
            TestOpenIdConnect.UseStaticConfiguration(services);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.PostConfigure<CookieAuthenticationOptions>(EntraSettings.CookieScheme, options => options.TimeProvider = Clock);
            services.AddSingleton<IStartupFilter>(new SignInStartup(_virtualDirectory));
        });
    }

    private sealed class SignInStartup : IStartupFilter
    {
        private readonly bool _virtualDirectory;

        public SignInStartup(bool virtualDirectory)
        {
            _virtualDirectory = virtualDirectory;
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                if (_virtualDirectory)
                {
                    app.UsePathBase("/PosterPrintRequest");
                }

                app.Use(async (context, nextMiddleware) =>
                {
                    if (context.Request.Path != SignInPath)
                    {
                        await nextMiddleware();
                        return;
                    }

                    var claims = new List<Claim>
                    {
                        new(PosterClaims.ObjectId, context.Request.Query["oid"].ToString()),
                        new(PosterClaims.PreferredUsername, "signed.in@test.example"),
                        new(PosterClaims.Name, "Signed In")
                    };
                    foreach (var role in context.Request.Query["roles"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        claims.Add(new Claim(PosterClaims.Roles, role));
                    }

                    if (context.Request.Query.TryGetValue("spoof", out var spoofed))
                    {
                        claims.Add(new Claim(PosterClaims.SessionExpiresAt, spoofed.ToString()));
                    }

                    var identity = new ClaimsIdentity(claims, "pwd", PosterClaims.Name, PosterClaims.Roles);
                    await context.SignInAsync(EntraSettings.CookieScheme, new ClaimsPrincipal(identity));
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                });
                next(app);
            };
    }
}

/// <summary>Authentication Contract sections 5.7 to 5.11 and 12: the session, circuit revalidation, and sign-out.</summary>
[Collection("Workflow database")]
public sealed class EntraSessionTests
{
    private const string CookieName = ".AspNetCore.Cookies";

    private readonly WorkflowDatabaseFixture _database;

    public EntraSessionTests(WorkflowDatabaseFixture database)
    {
        _database = database;
    }

    private static HttpClient Client(SessionFactory factory, string baseAddress = "http://localhost/") =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri(baseAddress)
        });

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string roles, string? spoof = null, string prefix = "")
    {
        var query = "?oid=" + InstitutionalTestIdentities.RequesterOid + "&roles=" + roles + (spoof is null ? "" : "&spoof=" + spoof);
        var response = await client.GetAsync(prefix + SessionFactory.SignInPath + query);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return response;
    }

    private static string AuthenticationCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';')[0])
            .Single(value => value.StartsWith(CookieName + "=", StringComparison.Ordinal))
            [(CookieName.Length + 1)..];

    private static bool SetsAuthenticationCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
        && values.Any(value => value.StartsWith(CookieName + "=", StringComparison.Ordinal));

    private static void AssertEntraChallenge(HttpResponseMessage response, string redirectUri)
    {
        Assert.True(HttpStatusCode.Redirect == response.StatusCode, response.StatusCode + " " + response.Content.ReadAsStringAsync().Result);
        var location = response.Headers.Location!;
        Assert.StartsWith(TestOpenIdConnect.AuthorizationEndpoint, location.OriginalString, StringComparison.Ordinal);
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("login", query["prompt"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
        Assert.Equal(TestEntra.ClientId, query["client_id"]);
        Assert.Equal(redirectUri, query["redirect_uri"]);
        Assert.Equal("openid profile", query["scope"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.False(string.IsNullOrEmpty(query["nonce"]));
        Assert.Null(query["client_secret"]);
        Assert.DoesNotContain("id_token", query["response_type"], StringComparison.Ordinal);
    }

    [Fact]
    public void Session_settings_are_set_explicitly()
    {
        using var factory = new SessionFactory(_database.ConnectionString);

        var cookie = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(EntraSettings.CookieScheme);
        var oidc = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(EntraSettings.ChallengeScheme);

        Assert.Equal(TimeSpan.FromMinutes(30), cookie.ExpireTimeSpan);
        Assert.False(cookie.SlidingExpiration);
        Assert.False(oidc.UseTokenLifetime);
        Assert.Equal("login", oidc.Prompt);
    }

    [Fact]
    public async Task The_cookie_expires_thirty_minutes_after_sign_in_and_records_the_instant_as_an_application_claim()
    {
        using var factory = new SessionFactory(_database.ConnectionString);
        using var client = Client(factory);
        var signedInAt = factory.Clock.GetUtcNow();

        var response = await SignInAsync(client, PosterRoles.Requester);

        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(EntraSettings.CookieScheme);
        var ticket = options.TicketDataFormat.Unprotect(AuthenticationCookie(response));
        Assert.NotNull(ticket);
        Assert.Equal(signedInAt, ticket.Properties.IssuedUtc);
        Assert.Equal(signedInAt.AddMinutes(30), ticket.Properties.ExpiresUtc);
        Assert.Equal(
            signedInAt.AddMinutes(30).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ticket.Principal.FindFirst(PosterClaims.SessionExpiresAt)!.Value);
        Assert.Equal(PosterClaims.Roles, ticket.Principal.Identities.Single().RoleClaimType);
    }

    [Fact]
    public async Task The_session_expiry_claim_is_set_by_the_application_and_never_taken_from_the_token()
    {
        using var factory = new SessionFactory(_database.ConnectionString);
        using var client = Client(factory);
        var signedInAt = factory.Clock.GetUtcNow();

        var response = await SignInAsync(client, PosterRoles.Requester, spoof: "99999999999");

        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(EntraSettings.CookieScheme);
        var claims = options.TicketDataFormat.Unprotect(AuthenticationCookie(response))!.Principal.FindAll(PosterClaims.SessionExpiresAt).ToList();
        Assert.Single(claims);
        Assert.Equal(signedInAt.AddMinutes(30).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), claims[0].Value);
    }

    [Fact]
    public async Task A_request_shortly_before_expiry_does_not_extend_it_and_a_request_after_expiry_is_challenged()
    {
        using var factory = new SessionFactory(_database.ConnectionString);
        using var client = Client(factory);
        await SignInAsync(client, PosterRoles.Requester);

        factory.Clock.Advance(TimeSpan.FromMinutes(29));
        var before = await client.GetAsync("/staff");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.False(SetsAuthenticationCookie(before), "A request must not renew the cookie.");

        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        var after = await client.GetAsync("/staff");
        AssertEntraChallenge(after, "http://localhost/signin-oidc");
    }

    [Fact]
    public async Task An_anonymous_entry_is_challenged_with_the_code_flow_pkce_and_prompt_login()
    {
        using var factory = new SessionFactory(_database.ConnectionString);
        using var client = Client(factory);

        AssertEntraChallenge(await client.GetAsync("/"), "http://localhost/signin-oidc");
        AssertEntraChallenge(await client.GetAsync("/request"), "http://localhost/signin-oidc");
    }

    [Fact]
    public async Task The_redirect_uri_carries_the_path_base_and_matches_the_production_redirect_uri()
    {
        using var factory = new SessionFactory(_database.ConnectionString, virtualDirectory: true);
        using var client = Client(factory, "https://posters.den.uic.edu/PosterPrintRequest/");

        var response = await client.GetAsync("/PosterPrintRequest/");

        AssertEntraChallenge(response, "https://posters.den.uic.edu/PosterPrintRequest/signin-oidc");
    }

    [Fact]
    public async Task A_signed_in_user_with_neither_role_is_sent_to_access_denied_under_the_path_base()
    {
        using var factory = new SessionFactory(_database.ConnectionString, virtualDirectory: true);
        using var client = Client(factory);
        await SignInAsync(client, roles: "", prefix: "/PosterPrintRequest");

        var response = await client.GetAsync("/PosterPrintRequest/staff");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/PosterPrintRequest/Account/AccessDenied", response.Headers.Location!.OriginalString);
        var denied = await client.GetStringAsync("/PosterPrintRequest/Account/AccessDenied");
        Assert.Contains("has not been assigned the Requester or Technician role", denied, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_out_requires_the_anti_forgery_token_removes_the_cookie_and_never_calls_the_end_session_endpoint()
    {
        using var factory = new SessionFactory(_database.ConnectionString, virtualDirectory: true);
        using var client = Client(factory);
        await SignInAsync(client, PosterRoles.Requester, prefix: "/PosterPrintRequest");

        var page = await client.GetStringAsync("/PosterPrintRequest/staff");
        Assert.Contains("action=\"/PosterPrintRequest/Account/SignOut\"", page, StringComparison.Ordinal);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token));

        var withoutToken = await client.PostAsync("/PosterPrintRequest/Account/SignOut", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        var forged = await client.PostAsync(
            "/PosterPrintRequest/Account/SignOut",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", token + "x")]));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/PosterPrintRequest/staff")).StatusCode);

        var signedOut = await client.PostAsync(
            "/PosterPrintRequest/Account/SignOut",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", token)]));

        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Equal("/PosterPrintRequest/", signedOut.Headers.Location!.OriginalString);
        Assert.DoesNotContain("login", signedOut.Headers.Location.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            signedOut.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(CookieName + "=;", StringComparison.Ordinal) && value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

        AssertEntraChallenge(
            await client.GetAsync("/PosterPrintRequest/"),
            "http://localhost/PosterPrintRequest/signin-oidc");
    }

    [Fact]
    public void No_signed_out_callback_path_and_no_end_session_redirect_exist()
    {
        using var factory = new SessionFactory(_database.ConnectionString);

        var oidc = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(EntraSettings.ChallengeScheme);

        Assert.False(oidc.SignedOutCallbackPath.HasValue);
        Assert.False(oidc.RemoteSignOutPath.HasValue);
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.WebProjectDirectory(), "Identity", "AccountEndpoints.cs"));
        Assert.DoesNotContain(EntraSettings.ChallengeScheme, source, StringComparison.Ordinal);
        Assert.DoesNotContain("SignOutAsync(EntraSettings.Challenge", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_out_is_a_post_only_endpoint()
    {
        using var factory = new SessionFactory(_database.ConnectionString);
        using var client = Client(factory);
        await SignInAsync(client, PosterRoles.Requester);

        var get = await client.GetAsync("/Account/SignOut");

        Assert.NotEqual(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/staff")).StatusCode);
    }

    [Fact]
    public void Circuits_use_the_revalidating_provider_with_a_one_minute_interval()
    {
        using var factory = new SessionFactory(_database.ConnectionString);
        using var scope = factory.Services.CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();

        Assert.IsType<SessionAuthenticationStateProvider>(provider);
        Assert.Equal(TimeSpan.FromMinutes(1), PosterSession.RevalidationInterval);
        Assert.Equal(TimeSpan.FromMinutes(1), new Probe(new ManualClock()).Interval);
    }

    [Fact]
    public async Task Revalidation_fails_once_the_expiry_instant_has_passed()
    {
        var clock = new ManualClock();
        var probe = new Probe(clock);
        var expires = clock.GetUtcNow().AddMinutes(30);
        var state = new AuthenticationState(Principal(expires.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));

        Assert.True(await probe.ValidateAsync(state));
        clock.Advance(TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(59));
        Assert.True(await probe.ValidateAsync(state));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(await probe.ValidateAsync(state));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.False(await probe.ValidateAsync(state));
    }

    [Fact]
    public async Task Revalidation_checks_the_instant_and_not_only_that_an_identity_exists()
    {
        var clock = new ManualClock();
        var probe = new Probe(clock);

        Assert.False(await probe.ValidateAsync(new AuthenticationState(Principal(expiry: null))));
        Assert.False(await probe.ValidateAsync(new AuthenticationState(Principal("not-a-number"))));
        Assert.False(await probe.ValidateAsync(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
        var unauthenticated = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PosterClaims.SessionExpiresAt, clock.GetUtcNow().AddHours(1).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))]));
        Assert.False(await probe.ValidateAsync(new AuthenticationState(unauthenticated)));
    }

    [Fact]
    public void Every_interactive_page_carries_the_session_guard()
    {
        var pages = Path.Combine(RepositoryPaths.WebProjectDirectory(), "Components", "Pages");
        var interactive = Directory.EnumerateFiles(pages, "*.razor")
            .Where(path => File.ReadAllText(path).Contains("@rendermode InteractiveServer", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order()
            .ToArray();

        Assert.Equal(
            ["Request.razor", "TechnicianConfiguration.razor", "TechnicianInventory.razor", "TechnicianLibrary.razor", "TechnicianWork.razor"],
            interactive);
        foreach (var page in interactive)
        {
            Assert.Contains("<SessionGuard />", File.ReadAllText(Path.Combine(pages, page!)), StringComparison.Ordinal);
        }
    }

    private static ClaimsPrincipal Principal(string? expiry)
    {
        var claims = new List<Claim> { new(PosterClaims.ObjectId, Guid.NewGuid().ToString()) };
        if (expiry is not null)
        {
            claims.Add(new Claim(PosterClaims.SessionExpiresAt, expiry));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies", PosterClaims.Name, PosterClaims.Roles));
    }

    private sealed class Probe : SessionAuthenticationStateProvider
    {
        public Probe(TimeProvider time)
            : base(new LoggerFactory(), time)
        {
        }

        public TimeSpan Interval => RevalidationInterval;

        public Task<bool> ValidateAsync(AuthenticationState state) => ValidateAuthenticationStateAsync(state, CancellationToken.None);
    }
}

public sealed class SessionGuardTests : BunitContext
{
    private readonly MutableAuthentication _authentication = new();

    public SessionGuardTests()
    {
        Services.AddSingleton<AuthenticationStateProvider>(_authentication);
        Services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new TestEnvironment("Development"));
    }

    [Fact]
    public void When_the_session_ends_the_circuit_navigates_to_the_application_entry_with_a_full_page_load()
    {
        var cut = Render<SessionGuard>();
        var navigation = Services.GetRequiredService<NavigationManager>();

        _authentication.Set(authenticated: true);
        Assert.Empty(((Bunit.TestDoubles.BunitNavigationManager)navigation).History);

        _authentication.Set(authenticated: false);

        Bunit.TestDoubles.NavigationHistory entry = null!;
        Bunit.TestDoubles.BunitNavigationManager fake = (Bunit.TestDoubles.BunitNavigationManager)navigation;
        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(fake.History);
            entry = fake.History.First();
        });
        Assert.Equal(fake.BaseUri, new Uri(new Uri(fake.BaseUri), entry.Uri).ToString());
        Assert.True(entry.Options.ForceLoad);
    }

    [Fact]
    public void The_sign_out_form_posts_with_an_anti_forgery_token_to_the_sign_out_endpoint()
    {
        Services.AddSingleton<Microsoft.AspNetCore.Components.Forms.AntiforgeryStateProvider>(new FixedAntiforgery());

        var cut = Render<SignOutForm>();

        var form = cut.Find("form");
        Assert.Equal("post", form.GetAttribute("method"));
        Assert.Equal("/Account/SignOut", form.GetAttribute("action"));
        Assert.Contains("__RequestVerificationToken", cut.Markup, StringComparison.Ordinal);
    }

    private sealed class FixedAntiforgery : Microsoft.AspNetCore.Components.Forms.AntiforgeryStateProvider
    {
        public override Microsoft.AspNetCore.Components.Forms.AntiforgeryRequestToken? GetAntiforgeryToken() =>
            new("test-request-token", "__RequestVerificationToken");
    }

    private sealed class MutableAuthentication : AuthenticationStateProvider
    {
        private AuthenticationState _state = Build(authenticated: true);

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);

        public void Set(bool authenticated)
        {
            _state = Build(authenticated);
            NotifyAuthenticationStateChanged(Task.FromResult(_state));
        }

        private static AuthenticationState Build(bool authenticated) =>
            new(new ClaimsPrincipal(authenticated
                ? new ClaimsIdentity([new Claim(PosterClaims.ObjectId, Guid.NewGuid().ToString())], "Cookies")
                : new ClaimsIdentity()));
    }
}
