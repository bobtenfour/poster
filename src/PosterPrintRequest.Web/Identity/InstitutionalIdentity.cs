using System.Globalization;
using System.Security.Claims;

namespace PosterPrintRequest.Web;

/// <summary>The two Entra App Roles (Authentication Contract section 4.1).</summary>
public static class PosterRoles
{
    public const string Requester = "Requester";

    public const string Technician = "Technician";
}

/// <summary>
/// Claim types exactly as Entra issues them (section 4.4), plus the one claim the application
/// itself adds at sign-in (section 5.9).
/// </summary>
public static class PosterClaims
{
    public const string ObjectId = "oid";

    public const string OnPremisesSecurityIdentifier = "onprem_sid";

    public const string PreferredUsername = "preferred_username";

    public const string Roles = "roles";

    public const string Name = "name";

    public const string SessionExpiresAt = "poster_session_expires_at";
}

/// <summary>
/// The person behind an operation. <see cref="ObjectId"/> is the canonical identity (section 3.1).
/// <see cref="OnPremisesSecurityIdentifier"/> is used only to link historical requests (section 3.4).
/// <see cref="PreferredUsername"/> is a display snapshot and never identifies or authorizes (section 3.8).
/// </summary>
public sealed record InstitutionalActor(Guid ObjectId, string? OnPremisesSecurityIdentifier, string? PreferredUsername)
{
    public bool IsValid => ObjectId != Guid.Empty;

    public static InstitutionalActor? Create(string? objectId, string? onPremisesSecurityIdentifier, string? preferredUsername)
    {
        if (!Guid.TryParse(objectId, CultureInfo.InvariantCulture, out var oid) || oid == Guid.Empty)
        {
            return null;
        }

        var sid = IsSecurityIdentifier(onPremisesSecurityIdentifier) ? onPremisesSecurityIdentifier : null;
        var name = preferredUsername?.Trim();
        return new InstitutionalActor(oid, sid, IsAccountName(name) ? name : null);
    }

    public static bool IsSecurityIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 8 or > 184)
        {
            return false;
        }

        if (!value.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hyphens = 0;
        for (var index = 4; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '-')
            {
                if (value[index - 1] == '-' || index == value.Length - 1)
                {
                    return false;
                }

                hyphens++;
                continue;
            }

            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return hyphens >= 2;
    }

    public static bool IsAccountName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value != value.Trim())
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>The single place where a principal becomes an actor (section 9).</summary>
public interface IInstitutionalActorSource
{
    InstitutionalActor? FromPrincipal(ClaimsPrincipal principal);
}

public sealed class ClaimsActorSource : IInstitutionalActorSource
{
    public InstitutionalActor? FromPrincipal(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return InstitutionalActor.Create(
            principal.FindFirst(PosterClaims.ObjectId)?.Value,
            principal.FindFirst(PosterClaims.OnPremisesSecurityIdentifier)?.Value,
            principal.FindFirst(PosterClaims.PreferredUsername)?.Value);
    }
}
