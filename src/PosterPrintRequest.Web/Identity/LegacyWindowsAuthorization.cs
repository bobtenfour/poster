using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authorization;

namespace PosterPrintRequest.Web;

// Authorization by Windows group membership. It is registered only in environments other than
// Production and Development (Authentication Contract section 5.2 and section 9); those
// environments keep their previous authentication registration unchanged.
public static class InstitutionalGroups
{
    public const string Requesters = "DEN-DentistryUsers";

    public const string Technicians = "GroupPCAdmins";
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

public interface IInstitutionalGroupVerifier
{
    bool IsMember(ClaimsPrincipal principal, string groupName);
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

internal static class InstitutionalMembership
{
    public static bool IsMember(IInstitutionalGroupVerifier groups, ClaimsPrincipal principal, string groupName)
    {
        try
        {
            return groups.IsMember(principal, groupName);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public sealed class InstitutionalGroupHandler : AuthorizationHandler<InstitutionalGroupRequirement>
{
    private readonly IInstitutionalGroupVerifier _groups;

    public InstitutionalGroupHandler(IInstitutionalGroupVerifier groups) => _groups = groups;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        InstitutionalGroupRequirement requirement)
    {
        if (InstitutionalMembership.IsMember(_groups, context.User, requirement.GroupName))
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
        if (InstitutionalMembership.IsMember(_groups, context.User, InstitutionalGroups.Requesters)
            || InstitutionalMembership.IsMember(_groups, context.User, InstitutionalGroups.Technicians))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
