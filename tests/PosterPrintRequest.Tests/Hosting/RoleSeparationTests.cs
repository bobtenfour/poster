using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Web;
using PosterPrintRequest.Tests.Workflow;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class RoleSeparationTests
{
    [Fact]
    public async Task Anonymous_entry_is_the_login_screen()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("/Account/Login", response.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("Log in", html, StringComparison.Ordinal);
        Assert.Contains("Username", html, StringComparison.Ordinal);
        Assert.Contains("plotter-technician.jpg", html, StringComparison.Ordinal);
        Assert.Contains("A technician printing a poster on a large-format plotter.", html, StringComparison.Ordinal);
        Assert.Contains("Session-only sign-in.", html, StringComparison.Ordinal);
        Assert.Contains("Self-registration is not available.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("What this system does", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Posters pending printing", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal activity", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task User_reaches_only_the_user_frontend()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = await PosterSignIn.SignInAsync(factory, "usera");
        var home = await client.GetAsync("/");
        home.EnsureSuccessStatusCode();
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("Posters Printed", html, StringComparison.Ordinal);
        Assert.Contains("Events Participated", html, StringComparison.Ordinal);
        Assert.Contains("Personal activity", html, StringComparison.Ordinal);
        Assert.Contains("Start a request", html, StringComparison.Ordinal);
        Assert.Contains(">Help<", html, StringComparison.Ordinal);
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
        Assert.Contains("Consumables needing attention", html, StringComparison.Ordinal);
        Assert.Contains(">Depleted<", html, StringComparison.Ordinal);
        Assert.Contains(">Low stock<", html, StringComparison.Ordinal);
        Assert.Contains(">Expiring soon<", html, StringComparison.Ordinal);
        Assert.Contains("Open inventory", html, StringComparison.Ordinal);
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
        Assert.Contains("Eagle 105", inventoryHtml, StringComparison.Ordinal);
        Assert.Contains(">None<", inventoryHtml, StringComparison.Ordinal);

        foreach (var path in new[] { "/", "/help", "/request" })
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
    public async Task Logout_ends_the_session()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = await PosterSignIn.SignInAsync(factory, "usera", allowAutoRedirect: false);
        var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var token = PosterSignIn.ReadToken(await home.Content.ReadAsStringAsync());
        var logout = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Contains("/Account/Login", logout.Headers.Location?.OriginalString, StringComparison.Ordinal);

        var again = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Contains("/Account/Login", again.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_login_stays_on_the_sign_in_screen()
    {
        using var factory = new PosterWebApplicationFactory();
        using var client = factory.CreateClient();
        var response = await PosterSignIn.PostCredentialsAsync(client, "usera", "wrong-password");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid login attempt.", html, StringComparison.Ordinal);
        Assert.Contains("/Account/Login", response.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal activity", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Posters pending printing", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_does_not_create_temporary_accounts()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<DemoEvaluationUsersOptions>(options =>
        {
            options.Enabled = true;
            options.Password = PosterSignIn.Password;
            options.UserNames = ["usera", "usero"];
        });
        using var provider = services.BuildServiceProvider();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };
        await DemoEvaluationUsersSeeder.SeedAsync(provider, environment, NullLogger.Instance);
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
            context.PosterRequests.Add(Poster(department.DepartmentId, reason.ReasonId, "POSTER-2032-000001", "usera", printed: true));
            context.PosterRequests.Add(Poster(department.DepartmentId, reason.ReasonId, "POSTER-2032-000002", "usero", printed: true));
            await context.SaveChangesAsync();
        }

        using var factory = new ConfiguredPosterFactory(_database.ConnectionString, Path.GetTempPath());
        using var client = await PosterSignIn.SignInAsync(factory, "usera");
        var html = await client.GetStringAsync("/");
        Assert.Contains("POSTER-2032-000001", html, StringComparison.Ordinal);
        Assert.Contains(eventName, html, StringComparison.Ordinal);
        Assert.Contains("id=\"stat-posters-printed\">1<", html, StringComparison.Ordinal);
        Assert.Contains("id=\"stat-events-participated\">1<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("POSTER-2032-000002", html, StringComparison.Ordinal);
    }

    private static PosterRequest Poster(int departmentId, int reasonId, string posterId, string userName, bool printed) => new()
    {
        PosterId = posterId,
        Name = "Activity Person",
        SubmittedByUserName = userName,
        DepartmentId = departmentId,
        ReasonId = reasonId,
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
