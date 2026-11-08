using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace PosterPrintRequest.Web;

public static class EntraSettings
{
    public const string TenantIdKey = "Authentication:TenantId";

    public const string ClientIdKey = "Authentication:ClientId";

    public const string ClientSecretFileKey = "Authentication:ClientSecretFile";

    public const string CallbackPath = "/signin-oidc";

    public const string Prompt = "login";

    public const string CookieScheme = "Cookies";

    public const string ChallengeScheme = "OpenIdConnect";
}

/// <summary>
/// Configures the OpenIdConnect challenge scheme (Authentication Contract sections 4.4, 5, and 6).
/// Production is a confidential client that redeems the code with the client secret read once from the
/// DPAPI file; Development is a public client that redeems it with the PKCE verifier only. There is no
/// fallback from one to the other.
/// </summary>
public sealed class EntraOpenIdConnectConfiguration : IConfigureNamedOptions<OpenIdConnectOptions>
{
    private readonly IConfiguration _configuration;
    private readonly bool _production;

    public EntraOpenIdConnectConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _production = environment.IsProduction();
    }

    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name != EntraSettings.ChallengeScheme)
        {
            return;
        }

        var tenant = _configuration[EntraSettings.TenantIdKey];
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new EntraConfigurationException("The Entra tenant identifier is not configured.");
        }

        if (!IsTenantIdentifier(tenant))
        {
            throw new EntraConfigurationException("The Entra tenant identifier is not valid.");
        }

        var client = _configuration[EntraSettings.ClientIdKey];
        if (string.IsNullOrWhiteSpace(client))
        {
            throw new EntraConfigurationException("The Entra client identifier is not configured.");
        }

        if (!Guid.TryParse(client, out _))
        {
            throw new EntraConfigurationException("The Entra client identifier is not valid.");
        }

        options.SignInScheme = EntraSettings.CookieScheme;
        options.Authority = "https://login.microsoftonline.com/" + tenant + "/v2.0";
        options.ClientId = client;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true;
        options.Prompt = EntraSettings.Prompt;
        options.CallbackPath = EntraSettings.CallbackPath;
        options.SignedOutCallbackPath = default;
        options.RemoteSignOutPath = default;
        options.UseTokenLifetime = false;
        options.MapInboundClaims = false;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.TokenValidationParameters.NameClaimType = PosterClaims.Name;
        options.TokenValidationParameters.RoleClaimType = PosterClaims.Roles;

        if (_production)
        {
            options.ClientSecret = EntraClientSecret.Read(_configuration[EntraSettings.ClientSecretFileKey]);
        }
    }

    private static bool IsTenantIdentifier(string value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-' && character != '.')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Checks the resulting options at startup (Authentication Contract section 5.14): the authorization
/// code flow with PKCE, the callback path, and prompt=login everywhere; the client secret in
/// Production; no client secret in Development.
/// </summary>
public sealed class EntraOpenIdConnectValidation : IValidateOptions<OpenIdConnectOptions>
{
    private readonly bool _production;

    public EntraOpenIdConnectValidation(IHostEnvironment environment)
    {
        _production = environment.IsProduction();
    }

    public ValidateOptionsResult Validate(string? name, OpenIdConnectOptions options)
    {
        if (name != EntraSettings.ChallengeScheme)
        {
            return ValidateOptionsResult.Skip;
        }

        if (options.ResponseType != OpenIdConnectResponseType.Code || !options.UsePkce)
        {
            return ValidateOptionsResult.Fail("Entra sign-in requires the authorization code flow with PKCE.");
        }

        if (options.CallbackPath != EntraSettings.CallbackPath || options.Prompt != EntraSettings.Prompt)
        {
            return ValidateOptionsResult.Fail("Entra sign-in requires the sign-in callback path and prompt=login.");
        }

        var hasSecret = !string.IsNullOrEmpty(options.ClientSecret);
        if (_production && !hasSecret)
        {
            return ValidateOptionsResult.Fail("Production does not accept the public-client configuration; the Entra client secret is required.");
        }

        if (!_production && hasSecret)
        {
            return ValidateOptionsResult.Fail("Development is a public client and uses no client secret.");
        }

        return ValidateOptionsResult.Success;
    }
}
