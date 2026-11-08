using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PosterPrintRequest.Web;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class InstitutionalEntryContractTests
{
    [Theory]
    [InlineData(false, true, InstitutionalEntry.StaffHome)]
    [InlineData(true, false, InstitutionalEntry.OperatorHome)]
    [InlineData(true, true, InstitutionalEntry.OperatorHome)]
    public async Task Destination_prefers_the_operator_area(bool isOperator, bool isStaff, string destination)
    {
        var authorization = new ScriptedAuthorization { Operator = isOperator, Staff = isStaff };

        var actual = await InstitutionalEntry.DestinationAsync(authorization, Authenticated());

        Assert.Equal(destination, actual);
    }

    [Fact]
    public async Task Missing_authentication_has_no_destination()
    {
        var authorization = new ScriptedAuthorization { Operator = true, Staff = true };

        var actual = await InstitutionalEntry.DestinationAsync(authorization, new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Null(actual);
    }

    [Fact]
    public async Task Neither_policy_has_no_destination()
    {
        var actual = await InstitutionalEntry.DestinationAsync(new ScriptedAuthorization(), Authenticated());

        Assert.Null(actual);
    }

    [Fact]
    public void Browser_paths_come_from_the_host_base()
    {
        Assert.Equal("/staff", ApplicationUris.BrowserPath("http://localhost/", "/staff"));
        Assert.Equal("/", ApplicationUris.BrowserPath("http://localhost/", "/"));
        Assert.Equal(
            "/PosterPrintRequest/css/design-system/tokens.css",
            ApplicationUris.BrowserPath("http://localhost/PosterPrintRequest/", "css/design-system/tokens.css"));
        Assert.Equal(
            "/PosterPrintRequest/technician/inventory#cartridges",
            ApplicationUris.BrowserPath("http://localhost/PosterPrintRequest/", "/technician/inventory#cartridges"));
        Assert.Equal("request/accepted/POSTER-2026-000001", ApplicationUris.Relative("/request/accepted/POSTER-2026-000001"));
    }

    private static ClaimsPrincipal Authenticated() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, @"DEMO\someone")], "Test"));

    private sealed class ScriptedAuthorization : IAuthorizationService
    {
        public bool Operator { get; init; }

        public bool Staff { get; init; }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            var allowed = policyName switch
            {
                PosterAccess.OperatorPolicy => Operator,
                PosterAccess.UserPolicy => Staff,
                _ => false
            };
            return Task.FromResult(allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        }
    }
}

public sealed class InstitutionalEntryHttpTests
{
    [Fact]
    public async Task Query_string_does_not_choose_the_area()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = await PosterSignIn.SignInAsync(factory, "both", allowAutoRedirect: false);

        var response = await client.GetAsync("/?area=staff");

        Assert.True((int)response.StatusCode is >= 300 and < 400, response.StatusCode.ToString());
        var location = response.Headers.Location?.OriginalString ?? "";
        Assert.Contains("/technician", location, StringComparison.Ordinal);
        Assert.DoesNotContain("/staff", location, StringComparison.Ordinal);
        Assert.DoesNotContain("area=staff", location, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal activity", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Identity_without_a_role_is_denied()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = await PosterSignIn.SignInAsync(factory, "neither");

        var response = await client.GetAsync("/");

        Assert.Contains("/Account/AccessDenied", response.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("has not been assigned the Requester or Technician role", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal activity", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Posters pending printing", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Identity_with_only_group_or_misplaced_role_claims_is_denied()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = await PosterSignIn.SignInAsync(factory, "neither");
        client.DefaultRequestHeaders.Add(
            InstitutionalTestHandler.ExtraClaimsHeader,
            "groups=" + InstitutionalGroups.Technicians + ";groups=" + InstitutionalGroups.Requesters
            + ";" + ClaimTypes.Role + "=" + PosterRoles.Technician
            + ";" + ClaimTypes.Role + "=" + PosterRoles.Requester);

        var response = await client.GetAsync("/staff");

        Assert.Contains("/Account/AccessDenied", response.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("has not been assigned the Requester or Technician role", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal activity", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Posters pending printing", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unusable_identity_is_challenged()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(InstitutionalTestHandler.OidHeader, "not-an-object-identifier");
        client.DefaultRequestHeaders.Add(InstitutionalTestHandler.UsernameHeader, "missing@test.example");

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("Personal activity", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Posters pending printing", html, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"password\"", html, StringComparison.OrdinalIgnoreCase);
    }
}
