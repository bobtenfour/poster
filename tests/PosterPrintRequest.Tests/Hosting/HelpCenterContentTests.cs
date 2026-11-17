using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Components.Pages;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class HelpCenterContentTests : BunitContext
{
    public HelpCenterContentTests()
    {
        Services.AddSingleton<IAuthorizationService>(new ClosedAuthorization());
    }

    [Fact]
    public void Staff_help_documents_the_submission_rules_and_stays_on_the_staff_route()
    {
        var layout = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Page("Help.razor"))!, "..", "Layout", "MainLayout.razor"));
        Assert.Contains("Href=\"/help\"", layout, StringComparison.Ordinal);
        Assert.Contains("UIC Dentistry School", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("Href=\"/technician/help\"", layout, StringComparison.Ordinal);
        Assert.DoesNotContain(">BlueIgnix<", layout, StringComparison.Ordinal);

        var source = File.ReadAllText(Page("Help.razor"));
        Assert.Contains("@page \"/help\"", source, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Policy = PosterAccess.UserPolicy)]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/technician/help", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BlueIgnix", source, StringComparison.Ordinal);

        var html = Render<Help>().Markup;
        Assert.Contains("Staff Help", html, StringComparison.Ordinal);
        Assert.Contains("UIC Dentistry School", html, StringComparison.Ordinal);
        Assert.Contains("Maximum width", html, StringComparison.Ordinal);
        Assert.Contains("36 inches", html, StringComparison.Ordinal);
        Assert.Contains("Maximum length", html, StringComparison.Ordinal);
        Assert.Contains("Longer than 72 inches is not accepted.", html, StringComparison.Ordinal);
        Assert.Contains("exactly one page", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly one slide", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("This application creates the TDX ticket for each accepted request.", html, StringComparison.Ordinal);
        Assert.Contains("id=\"poster-size\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/help#poster-file\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("BlueIgnix", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/technician/help", html, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\posters", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Operator_help_documents_the_work_stages_and_stays_on_the_operator_route()
    {
        var layout = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Page("OperatorHelp.razor"))!, "..", "Layout", "TechnicianLayout.razor"));
        Assert.Contains("Href=\"/technician/help\"", layout, StringComparison.Ordinal);
        Assert.Contains("UIC Dentistry School", layout, StringComparison.Ordinal);
        Assert.DoesNotContain(">BlueIgnix<", layout, StringComparison.Ordinal);

        var source = File.ReadAllText(Page("OperatorHelp.razor"));
        Assert.Contains("@page \"/technician/help\"", source, StringComparison.Ordinal);
        Assert.Contains("@layout TechnicianLayout", source, StringComparison.Ordinal);
        Assert.Contains("[Authorize(Policy = PosterAccess.OperatorPolicy)]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/help\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BlueIgnix", source, StringComparison.Ordinal);

        var html = Render<OperatorHelp>().Markup;
        Assert.Contains("Operator Help", html, StringComparison.Ordinal);
        Assert.Contains("UIC Dentistry School", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/technician/help#stages\"", html, StringComparison.Ordinal);
        Assert.Contains("Mark printed", html, StringComparison.Ordinal);
        Assert.Contains("Printer unavailable", html, StringComparison.Ordinal);
        Assert.Contains("WITHOUT-EVENT TO BE PRINTED", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Staff Help", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/help\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("BlueIgnix", html, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\posters", html, StringComparison.OrdinalIgnoreCase);
    }

    private static string Page(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "PosterPrintRequest.Web", "Components", "Pages", fileName)))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "PosterPrintRequest.Web", "Components", "Pages", fileName);
    }

    private sealed class ClosedAuthorization : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(AuthorizationResult.Failed());
    }
}
