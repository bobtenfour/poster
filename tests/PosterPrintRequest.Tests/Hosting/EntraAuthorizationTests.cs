using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PosterPrintRequest.Web;

namespace PosterPrintRequest.Tests.Hosting;

/// <summary>Authentication Contract sections 4 and 12: the three policies, built on the roles claim.</summary>
public sealed class EntraAuthorizationTests
{
    private static ServiceProvider Build(string environment)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPosterAuthentication(new ConfigurationBuilder().Build(), new TestEnvironment(environment));
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Person(params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(PosterClaims.ObjectId, Guid.NewGuid().ToString()),
            new(PosterClaims.PreferredUsername, "someone@test.example")
        };
        claims.AddRange(roles.Select(role => new Claim(PosterClaims.Roles, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies", PosterClaims.Name, PosterClaims.Roles));
    }

    private static async Task<(bool User, bool Operator, bool Fallback)> EvaluateAsync(ServiceProvider provider, ClaimsPrincipal principal)
    {
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var fallback = await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync();
        Assert.NotNull(fallback);
        return (
            (await authorization.AuthorizeAsync(principal, PosterAccess.UserPolicy)).Succeeded,
            (await authorization.AuthorizeAsync(principal, PosterAccess.OperatorPolicy)).Succeeded,
            (await authorization.AuthorizeAsync(principal, null, fallback.Requirements)).Succeeded);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Requester_role_opens_only_the_requester_policy_and_the_fallback(string environment)
    {
        using var provider = Build(environment);

        Assert.Equal((true, false, true), await EvaluateAsync(provider, Person(PosterRoles.Requester)));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Technician_role_does_not_imply_the_requester_role(string environment)
    {
        using var provider = Build(environment);

        Assert.Equal((false, true, true), await EvaluateAsync(provider, Person(PosterRoles.Technician)));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task A_multi_valued_roles_claim_is_one_role_per_value_and_each_policy_is_independent(string environment)
    {
        using var provider = Build(environment);

        Assert.Equal((true, true, true), await EvaluateAsync(provider, Person(PosterRoles.Requester, PosterRoles.Technician)));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Authenticated_without_either_role_satisfies_no_policy(string environment)
    {
        using var provider = Build(environment);

        Assert.Equal((false, false, false), await EvaluateAsync(provider, Person()));
        Assert.Equal((false, false, false), await EvaluateAsync(provider, Person("SomeOtherRole")));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Group_claims_and_windows_group_membership_are_not_an_authority(string environment)
    {
        using var provider = Build(environment);
        var groupsOnly = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(PosterClaims.ObjectId, Guid.NewGuid().ToString()),
                new Claim("groups", Guid.NewGuid().ToString()),
                new Claim("groups", "DEN-DentistryUsers"),
                new Claim("groups", "GroupPCAdmins"),
                new Claim(ClaimTypes.GroupSid, "S-1-5-21-1-2-3-513"),
                new Claim(ClaimTypes.Role, PosterRoles.Requester),
                new Claim(ClaimTypes.Role, PosterRoles.Technician)
            ],
            "Cookies",
            PosterClaims.Name,
            PosterClaims.Roles));

        Assert.Equal((false, false, false), await EvaluateAsync(provider, groupsOnly));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Role_claims_without_authentication_open_nothing(string environment)
    {
        using var provider = Build(environment);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PosterClaims.Roles, PosterRoles.Requester), new Claim(PosterClaims.Roles, PosterRoles.Technician)],
            authenticationType: null,
            PosterClaims.Name,
            PosterClaims.Roles));

        Assert.Equal((false, false, false), await EvaluateAsync(provider, anonymous));
    }

    [Theory]
    [InlineData(new[] { PosterRoles.Requester, PosterRoles.Technician }, InstitutionalEntry.OperatorHome)]
    [InlineData(new[] { PosterRoles.Technician }, InstitutionalEntry.OperatorHome)]
    [InlineData(new[] { PosterRoles.Requester }, InstitutionalEntry.StaffHome)]
    [InlineData(new string[0], null)]
    public async Task Entry_prefers_the_technician_area_and_has_no_destination_without_a_role(string[] roles, string? destination)
    {
        using var provider = Build("Production");

        var actual = await InstitutionalEntry.DestinationAsync(
            provider.GetRequiredService<IAuthorizationService>(),
            Person(roles));

        Assert.Equal(destination, actual);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Entra_environments_register_cookie_and_openid_connect_and_no_windows_authority(string environment)
    {
        using var provider = Build(environment);

        var schemes = (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync())
            .Select(scheme => scheme.Name)
            .Order()
            .ToArray();
        Assert.Equal(["Cookies", "OpenIdConnect"], schemes);
        Assert.DoesNotContain(NegotiateDefaults.AuthenticationScheme, schemes);
        var defaults = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthenticationOptions>>().Value;
        Assert.Equal("Cookies", defaults.DefaultScheme);
        Assert.Equal("OpenIdConnect", defaults.DefaultChallengeScheme);

        Assert.Null(provider.GetService<IInstitutionalGroupVerifier>());
        Assert.DoesNotContain(
            provider.GetServices<IAuthorizationHandler>(),
            handler => handler is InstitutionalGroupHandler or InstitutionalParticipantHandler);
        Assert.IsType<ClaimsActorSource>(provider.GetRequiredService<IInstitutionalActorSource>());
        Assert.IsType<InstitutionalAuthorizationResultHandler>(provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>());
    }

    [Fact]
    public async Task Other_environments_keep_their_previous_windows_registration_unchanged()
    {
        using var provider = Build("Demo");

        var schemes = (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync())
            .Select(scheme => scheme.Name)
            .ToArray();
        Assert.Equal([NegotiateDefaults.AuthenticationScheme], schemes);
        Assert.IsType<WindowsGroupVerifier>(provider.GetRequiredService<IInstitutionalGroupVerifier>());
        Assert.Equal(
            2,
            provider.GetServices<IAuthorizationHandler>().Count(handler => handler is InstitutionalGroupHandler or InstitutionalParticipantHandler));
    }

    [Fact]
    public void Windows_group_names_exist_only_in_the_legacy_registration_file()
    {
        var source = Path.Combine(RepositoryPaths.Root(), "src");
        var offenders = Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".razor", StringComparison.Ordinal))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("DEN-DentistryUsers", StringComparison.Ordinal)
                || File.ReadAllText(path).Contains("GroupPCAdmins", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .ToArray();

        Assert.Equal(["LegacyWindowsAuthorization.cs"], offenders);
    }
}
