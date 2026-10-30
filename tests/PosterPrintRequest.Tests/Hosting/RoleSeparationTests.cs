using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Web;
using PosterPrintRequest.Tests.Workflow;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class RoleSeparationTests
{
    [Fact]
    public async Task Anonymous_entry_is_challenged_without_a_password_form()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("type=\"password\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invalid login attempt.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal activity", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Posters pending printing", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task User_reaches_only_the_user_frontend()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = await PosterSignIn.SignInAsync(factory, "usera");
        var home = await client.GetAsync("/");
        home.EnsureSuccessStatusCode();
        Assert.EndsWith("/staff", home.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("Posters Printed", html, StringComparison.Ordinal);
        Assert.Contains("Events Participated", html, StringComparison.Ordinal);
        Assert.Contains("Personal activity", html, StringComparison.Ordinal);
        Assert.Contains("Start a request", html, StringComparison.Ordinal);
        Assert.Contains(">Help<", html, StringComparison.Ordinal);
        Assert.Contains(">Service<", html, StringComparison.Ordinal);
        Assert.Contains(">Request<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/technician\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/technician/inventory\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Posters pending printing", html, StringComparison.Ordinal);

        var denied = await client.GetAsync("/technician");
        Assert.Contains("/Account/AccessDenied", denied.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var deniedHtml = await denied.Content.ReadAsStringAsync();
        Assert.Contains("Your account cannot open this area.", deniedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Posters pending printing", deniedHtml, StringComparison.Ordinal);

        var library = await client.GetAsync("/technician/library");
        Assert.Contains("/Account/AccessDenied", library.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var inventory = await client.GetAsync("/technician/inventory");
        Assert.Contains("/Account/AccessDenied", inventory.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var configuration = await client.GetAsync("/technician/configuration");
        Assert.Contains("/Account/AccessDenied", configuration.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var file = await client.GetAsync("/technician/files/POSTER-2026-000001/poster");
        Assert.Contains("/Account/AccessDenied", file.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.DoesNotContain("%PDF", await file.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Operator_reaches_only_the_technician_frontend()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = await PosterSignIn.SignInAsync(factory, "usero");
        var home = await client.GetAsync("/technician");
        home.EnsureSuccessStatusCode();
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("Posters pending printing", html, StringComparison.Ordinal);
        Assert.Contains("Posters printed this month", html, StringComparison.Ordinal);
        Assert.Contains("Posters awaiting pickup", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/technician/library\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/technician/inventory\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/technician/configuration\"", html, StringComparison.Ordinal);
        Assert.Contains("Printing Supplies", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Dashboard\"", html, StringComparison.Ordinal);
        Assert.Contains("<h1>Dashboard</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<title>Dashboard</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Operations<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Operations\"", html, StringComparison.Ordinal);
        Assert.Contains("Monitor toner, paper, and lamination stock.", html, StringComparison.Ordinal);
        Assert.Contains(">Needs Reorder<", html, StringComparison.Ordinal);
        Assert.Contains("Items at or below reorder threshold.", html, StringComparison.Ordinal);
        Assert.Contains(">Expiring Soon<", html, StringComparison.Ordinal);
        Assert.Contains("Items approaching expiration date.", html, StringComparison.Ordinal);
        Assert.Contains("Ink Cartridges", html, StringComparison.Ordinal);
        Assert.Contains(">Paper<", html, StringComparison.Ordinal);
        Assert.Contains("Lamination Materials", html, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(html, "href=\"/technician/inventory#cartridges\""));
        Assert.Equal(2, CountOccurrences(html, "href=\"/technician/inventory#paper\""));
        Assert.Equal(2, CountOccurrences(html, "href=\"/technician/inventory#laminating\""));
        Assert.Contains("View Inventory", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/technician/inventory\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Depleted<", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Low Stock<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Needs attention", html, StringComparison.Ordinal);
        Assert.DoesNotContain("C9403A", html, StringComparison.Ordinal);
        Assert.Contains("usero", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/help\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Request<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal activity", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Students and staff", html, StringComparison.Ordinal);

        var inventoryPage = await client.GetAsync("/technician/inventory");
        inventoryPage.EnsureSuccessStatusCode();
        var inventoryHtml = await inventoryPage.Content.ReadAsStringAsync();
        Assert.Contains("C9403A", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains("C9370A", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains("C9371A", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains("C9372A", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains("C9373A", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains("C9374A", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains("C1861A", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains("C6814A", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains(">Cartridges<", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains(">Paper<", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains(">Laminating materials<", inventoryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Eagle 105", inventoryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("HP 72", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains(">None<", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains("Add stock", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains("Remove stock", inventoryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Warning period", inventoryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Expiration warning", inventoryHtml, StringComparison.Ordinal);

        var entry = await client.GetAsync("/");
        entry.EnsureSuccessStatusCode();
        Assert.EndsWith("/technician", entry.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var entryHtml = await entry.Content.ReadAsStringAsync();
        Assert.Contains("Posters pending printing", entryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Staff<", entryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal activity", entryHtml, StringComparison.Ordinal);

        foreach (var path in new[] { "/staff", "/help", "/request" })
        {
            var denied = await client.GetAsync(path);
            Assert.Contains("/Account/AccessDenied", denied.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            var deniedHtml = await denied.Content.ReadAsStringAsync();
            Assert.Contains("Your account cannot open this area.", deniedHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"requester-name\"", deniedHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("Personal activity", deniedHtml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Neither_group_is_denied_and_one_group_does_not_grant_the_other()
    {
        using var factory = new PosterWebApplicationFactory();
        using var neither = await PosterSignIn.SignInAsync(factory, "neither");
        var denied = await neither.GetAsync("/");
        Assert.Contains("/Account/AccessDenied", denied.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var deniedHtml = await denied.Content.ReadAsStringAsync();
        Assert.Contains("not in an authorized group", deniedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal activity", deniedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Posters pending printing", deniedHtml, StringComparison.Ordinal);

        var technicianDenied = await neither.GetAsync("/technician");
        Assert.Contains("/Account/AccessDenied", technicianDenied.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);

        using var both = await PosterSignIn.SignInAsync(factory, "both");
        var entryBoth = await both.GetAsync("/");
        entryBoth.EnsureSuccessStatusCode();
        Assert.EndsWith("/technician", entryBoth.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var entryBothHtml = await entryBoth.Content.ReadAsStringAsync();
        Assert.Contains("Posters pending printing", entryBothHtml, StringComparison.Ordinal);
        Assert.Contains(">Staff<", entryBothHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"/staff\"", entryBothHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal activity", entryBothHtml, StringComparison.Ordinal);

        var staff = await both.GetAsync("/staff");
        staff.EnsureSuccessStatusCode();
        var staffHtml = await staff.Content.ReadAsStringAsync();
        Assert.Contains("Personal activity", staffHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"/technician\"", staffHtml, StringComparison.Ordinal);

        var technician = await both.GetAsync("/technician");
        technician.EnsureSuccessStatusCode();
        var technicianHtml = await technician.Content.ReadAsStringAsync();
        Assert.Contains("Posters pending printing", technicianHtml, StringComparison.Ordinal);
        Assert.Contains(">Staff<", technicianHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Requester home", technicianHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_group_verification_does_not_accept_a_claim_that_is_not_a_windows_identity()
    {
        var verifier = new WindowsGroupVerifier();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, @"UNIVERSITY\someone"),
                new Claim(ClaimTypes.Role, InstitutionalGroups.Technicians),
                new Claim(ClaimTypes.Role, InstitutionalGroups.Requesters)
            ],
            "Test"));

        Assert.False(verifier.IsMember(principal, InstitutionalGroups.Technicians));
        Assert.False(verifier.IsMember(principal, InstitutionalGroups.Requesters));
        Assert.False(new WindowsActorSource().FromPrincipal(principal)?.IsValid == true);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}

[Collection("Workflow database")]
public sealed class UserActivityTests
{
    private readonly WorkflowDatabaseFixture _database;

    public UserActivityTests(WorkflowDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Personal_activity_counts_only_the_signed_in_users_posters()
    {
        var eventName = "Activity event " + Guid.NewGuid().ToString("N");
        await using (var context = _database.CreateContext())
        {
            var department = new Department { Name = "Activity " + Guid.NewGuid().ToString("N") };
            var reason = new Reason { Name = eventName, RequiresMentor = false, RequiresApprovalSheet = false };
            context.Departments.Add(department);
            context.Reasons.Add(reason);
            await context.SaveChangesAsync();
            context.PosterRequests.Add(Poster(department.DepartmentId, department.Name, reason.ReasonId, eventName, "POSTER-2032-000001", "usera", printed: true));
            context.PosterRequests.Add(Poster(department.DepartmentId, department.Name, reason.ReasonId, eventName, "POSTER-2032-000002", "usero", printed: true));
            await context.SaveChangesAsync();
        }

        using var factory = new ConfiguredPosterFactory(_database.ConnectionString, Path.GetTempPath());
        using var client = await PosterSignIn.SignInAsync(factory, "usera");
        await using var verification = _database.CreateContext();
        var owned = await verification.PosterRequests
            .AsNoTracking()
            .Where(request => request.SubmittedBySecurityIdentifier == InstitutionalTestIdentities.RequesterSid)
            .Select(request => new { request.ReasonName, request.PosterProcessing.Printed })
            .ToListAsync();
        var expectedPrinted = owned.Count(poster => poster.Printed);
        Assert.True(expectedPrinted >= 1);
        var expectedEvents = owned
            .Select(poster => poster.ReasonName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var html = await client.GetStringAsync("/");
        Assert.Contains("POSTER-2032-000001", html, StringComparison.Ordinal);
        Assert.Contains(eventName, html, StringComparison.Ordinal);
        Assert.Contains("id=\"stat-posters-printed\">" + expectedPrinted + "<", html, StringComparison.Ordinal);
        Assert.Contains("id=\"stat-events-participated\">" + expectedEvents + "<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("POSTER-2032-000002", html, StringComparison.Ordinal);
    }

    private static PosterRequest Poster(int departmentId, string departmentName, int reasonId, string reasonName, string posterId, string userName, bool printed) => new()
    {
        PosterId = posterId,
        Name = "Activity Person",
        SubmittedByAccountName = userName == "usera"
            ? InstitutionalTestIdentities.RequesterAccount
            : InstitutionalTestIdentities.TechnicianAccount,
        SubmittedBySecurityIdentifier = userName == "usera"
            ? InstitutionalTestIdentities.RequesterSid
            : InstitutionalTestIdentities.TechnicianSid,
        DepartmentId = departmentId,
        DepartmentName = departmentName,
        ReasonId = reasonId,
        ReasonName = reasonName,
        Room = "4",
        Phone = "555-0140",
        Email = "activity@example.edu",
        DateIn = new DateTime(2026, 10, 6, 9, 0, 0),
        PosterFile = new PosterFile
        {
            OriginalFileName = "Poster.pdf",
            DetectedFormat = "PDF",
            PageCount = 1,
            Width = 24,
            Length = 36,
            StoragePath = "WITHOUT-EVENT/2026/" + posterId + "/Poster.pdf"
        },
        PosterProcessing = new PosterProcessing { Printed = printed }
    };
}
