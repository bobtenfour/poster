using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;

namespace PosterPrintRequest.Web;

public static class InstitutionalGroups
{
    public const string Requesters = "DEN-DentistryUsers";

    public const string Technicians = "GroupPCAdmins";
}

public sealed record InstitutionalActor(string SecurityIdentifier, string AccountName)
{
    public bool IsValid => IsSecurityIdentifier(SecurityIdentifier) && IsAccountName(AccountName);

    public static InstitutionalActor? Create(string? securityIdentifier, string? accountName)
    {
        var account = accountName?.Trim();
        if (!IsSecurityIdentifier(securityIdentifier) || !IsAccountName(account))
        {
            return null;
        }

        return new InstitutionalActor(securityIdentifier!, account!);
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

public static class InstitutionalGroupNames
{
    public static bool Matches(string? translatedAccount, string groupName)
    {
        if (string.IsNullOrWhiteSpace(translatedAccount) || string.IsNullOrWhiteSpace(groupName))
        {
            return false;
        }

        if (translatedAccount.Equals(groupName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var separator = translatedAccount.LastIndexOf('\\');
        if (separator < 0 || separator >= translatedAccount.Length - 1)
        {
            return false;
        }

        return translatedAccount[(separator + 1)..].Equals(groupName, StringComparison.OrdinalIgnoreCase);
    }
}

public interface IInstitutionalActorSource
{
    InstitutionalActor? FromPrincipal(ClaimsPrincipal principal);
}

public interface IInstitutionalGroupVerifier
{
    bool IsMember(ClaimsPrincipal principal, string groupName);
}

public sealed class WindowsActorSource : IInstitutionalActorSource
{
    public InstitutionalActor? FromPrincipal(ClaimsPrincipal principal)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return FromWindowsPrincipal(principal);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static InstitutionalActor? FromWindowsPrincipal(ClaimsPrincipal principal)
    {
        if (principal.Identity is not WindowsIdentity windows || !windows.IsAuthenticated)
        {
            return null;
        }

        return InstitutionalActor.Create(windows.User?.Value, windows.Name);
    }
}

public sealed class WindowsGroupVerifier : IInstitutionalGroupVerifier
{
    public bool IsMember(ClaimsPrincipal principal, string groupName)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(groupName))
        {
            return false;
        }

        return IsWindowsMember(principal, groupName);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsWindowsMember(ClaimsPrincipal principal, string groupName)
    {
        if (principal.Identity is not WindowsIdentity windows || !windows.IsAuthenticated)
        {
            return false;
        }

        try
        {
            if (new WindowsPrincipal(windows).IsInRole(groupName))
            {
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }

        if (windows.Groups is null)
        {
            return false;
        }

        foreach (var group in windows.Groups)
        {
            string translated;
            try
            {
                translated = group.Translate(typeof(NTAccount)).Value;
            }
            catch (IdentityNotMappedException)
            {
                continue;
            }
            catch (Exception)
            {
                return false;
            }

            if (InstitutionalGroupNames.Matches(translated, groupName))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class InstitutionalGroupRequirement : IAuthorizationRequirement
{
    public InstitutionalGroupRequirement(string groupName) => GroupName = groupName;

    public string GroupName { get; }
}

public sealed class InstitutionalParticipantRequirement : IAuthorizationRequirement;

public sealed class InstitutionalGroupHandler : AuthorizationHandler<InstitutionalGroupRequirement>
{
    private readonly IInstitutionalGroupVerifier _groups;

    public InstitutionalGroupHandler(IInstitutionalGroupVerifier groups) => _groups = groups;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        InstitutionalGroupRequirement requirement)
    {
        if (_groups.IsMember(context.User, requirement.GroupName))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public sealed class InstitutionalParticipantHandler : AuthorizationHandler<InstitutionalParticipantRequirement>
{
    private readonly IInstitutionalGroupVerifier _groups;

    public InstitutionalParticipantHandler(IInstitutionalGroupVerifier groups) => _groups = groups;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        InstitutionalParticipantRequirement requirement)
    {
        if (_groups.IsMember(context.User, InstitutionalGroups.Requesters)
            || _groups.IsMember(context.User, InstitutionalGroups.Technicians))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
