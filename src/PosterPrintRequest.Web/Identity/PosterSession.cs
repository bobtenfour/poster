using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace PosterPrintRequest.Web;

/// <summary>
/// The absolute 30-minute session (Authentication Contract sections 5.7 and 5.9). The expiry instant
/// is recorded at sign-in as an application claim; it never comes from Entra.
/// </summary>
public static class PosterSession
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan RevalidationInterval = TimeSpan.FromMinutes(1);

    public static Task RecordExpiryAsync(CookieSigningInContext context)
    {
        var clock = context.Options.TimeProvider ?? TimeProvider.System;
        var issued = context.Properties.IssuedUtc ?? clock.GetUtcNow();
        var expires = issued.Add(context.Options.ExpireTimeSpan);
        context.Properties.IssuedUtc = issued;
        context.Properties.ExpiresUtc = expires;

        var identity = context.Principal?.Identities.FirstOrDefault();
        if (identity is not null)
        {
            foreach (var existing in identity.FindAll(PosterClaims.SessionExpiresAt).ToList())
            {
                identity.RemoveClaim(existing);
            }

            identity.AddClaim(new Claim(
                PosterClaims.SessionExpiresAt,
                expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64));
        }

        return Task.CompletedTask;
    }

    public static bool IsActive(ClaimsPrincipal user, DateTimeOffset now)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var value = user.FindFirst(PosterClaims.SessionExpiresAt)?.Value;
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && now < DateTimeOffset.FromUnixTimeSeconds(seconds);
    }
}

/// <summary>
/// Interactive circuits keep the authentication state they started with. This provider compares the
/// current time with the session-expiry claim every minute (Authentication Contract section 5.9) and,
/// once the instant has passed, turns the circuit's state unauthenticated.
/// </summary>
public class SessionAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly TimeProvider _time;

    public SessionAuthenticationStateProvider(ILoggerFactory loggerFactory, TimeProvider time)
        : base(loggerFactory)
    {
        _time = time;
    }

    protected override TimeSpan RevalidationInterval => PosterSession.RevalidationInterval;

    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken) =>
        Task.FromResult(PosterSession.IsActive(authenticationState.User, _time.GetUtcNow()));
}
