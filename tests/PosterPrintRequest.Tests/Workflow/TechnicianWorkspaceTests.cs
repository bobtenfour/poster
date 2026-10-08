using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Tests.Hosting;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

[Collection("Workflow database")]
public sealed class TechnicianWorkspaceTests : IDisposable
{
    private readonly WorkflowDatabaseFixture _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poster-workspace-" + Guid.NewGuid().ToString("N"));

    public TechnicianWorkspaceTests(WorkflowDatabaseFixture database)
    {
        _database = database;
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Operator_workspace_shows_the_queue_library_and_files()
    {
        using var factory = new ConfiguredPosterFactory(_database.ConnectionString, _root);
        Directory.CreateDirectory(Path.Combine(_root, "drafts"));
        File.WriteAllText(Path.Combine(_root, "drafts", "secret.txt"), "draft-secret");
        var posterFolder = Path.Combine(_root, StorageNames.WithoutEvent, "2031", "Queue Person - POSTER-2031-000002");
        Directory.CreateDirectory(posterFolder);
        var posterBytes = "poster-bytes"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(posterFolder, StorageNames.PosterPdf), posterBytes);
        await File.WriteAllBytesAsync(Path.Combine(posterFolder, StorageNames.ApprovalSheet), "sheet-bytes"u8.ToArray());
        Directory.CreateDirectory(Path.Combine(_root, StorageNames.WithoutEvent, "10"));
        Directory.CreateDirectory(Path.Combine(_root, StorageNames.Events, "Example event 2031", "Queue Person - POSTER-2031-000003"));
        Directory.CreateDirectory(Path.Combine(_root, "Example event"));

        int departmentId;
        await using (var context = _database.CreateContext())
        {
            var department = new Department { Name = "Workspace " + Guid.NewGuid().ToString("N") };
            context.Departments.Add(department);
            await context.SaveChangesAsync();
            departmentId = department.DepartmentId;
            context.PosterRequests.Add(Request(departmentId, "POSTER-2031-000001", "Sloane Harper", new DateOnly(2026, 10, 4), printed: true, dateOut: new DateOnly(2026, 10, 5)));
            context.PosterRequests.Add(Request(departmentId, "POSTER-2031-000002", null, null, printed: false, dateOut: null, storagePath: "WITHOUT-EVENT/2031/Queue Person - POSTER-2031-000002/Poster.pdf", approvalPath: "WITHOUT-EVENT/2031/Queue Person - POSTER-2031-000002/Approval-Sheet.pdf"));
            await context.SaveChangesAsync();
        }

        using var allowed = await PosterSignIn.SignInAsync(factory, "usero");
        var dashboard = await allowed.GetAsync("/technician?period=2026-10&posterId=POSTER-2031-000001");
        dashboard.EnsureSuccessStatusCode();
        var dashboardHtml = await dashboard.Content.ReadAsStringAsync();
        Assert.Contains("Posters pending printing", dashboardHtml, StringComparison.Ordinal);
        Assert.Contains("Posters printed this month", dashboardHtml, StringComparison.Ordinal);
        Assert.Contains("Posters printed this year", dashboardHtml, StringComparison.Ordinal);
        Assert.Contains("Posters awaiting pickup", dashboardHtml, StringComparison.Ordinal);
        Assert.Contains("Total posters processed", dashboardHtml, StringComparison.Ordinal);
        Assert.Contains("October 2026", dashboardHtml, StringComparison.Ordinal);
        Assert.Contains("Sloane Harper", dashboardHtml, StringComparison.Ordinal);
        Assert.Contains("POSTER-2031-000002", dashboardHtml, StringComparison.Ordinal);
        Assert.Contains("Open poster", dashboardHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"/technician/work/POSTER-2031-000001\"", dashboardHtml, StringComparison.Ordinal);
        Assert.Contains("usero", dashboardHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, dashboardHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("draft-secret", dashboardHtml, StringComparison.Ordinal);

        var september = await allowed.GetAsync("/technician?period=2026-09");
        september.EnsureSuccessStatusCode();
        var septemberHtml = await september.Content.ReadAsStringAsync();
        Assert.Contains("September 2026", septemberHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Sloane Harper", septemberHtml, StringComparison.Ordinal);

        var invalid = await allowed.GetAsync("/technician?posterId=not-a-poster");
        invalid.EnsureSuccessStatusCode();
        Assert.Contains("Enter a Poster ID such as POSTER-2026-000001.", await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var missing = await allowed.GetAsync("/technician?posterId=POSTER-1999-000001");
        missing.EnsureSuccessStatusCode();
        Assert.Contains("No request uses that Poster ID.", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var library = await allowed.GetAsync("/technician/library");
        library.EnsureSuccessStatusCode();
        var libraryHtml = await library.Content.ReadAsStringAsync();
        Assert.Contains("epx-folder-card", libraryHtml, StringComparison.Ordinal);
        Assert.Contains("EVENTS", libraryHtml, StringComparison.Ordinal);
        Assert.Contains("WITHOUT-EVENT", libraryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Example event", libraryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(">drafts<", libraryHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, libraryHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("draft-secret", libraryHtml, StringComparison.Ordinal);

        var eventsPage = await allowed.GetAsync("/technician/library/EVENTS");
        eventsPage.EnsureSuccessStatusCode();
        Assert.Contains("Example event 2031", await eventsPage.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var yearsPage = await allowed.GetAsync("/technician/library/WITHOUT-EVENT");
        yearsPage.EnsureSuccessStatusCode();
        var yearsHtml = await yearsPage.Content.ReadAsStringAsync();
        Assert.Contains("2031", yearsHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(">10<", yearsHtml, StringComparison.Ordinal);

        var posterPage = await allowed.GetAsync("/technician/library/WITHOUT-EVENT/2031/Queue%20Person%20-%20POSTER-2031-000002");
        posterPage.EnsureSuccessStatusCode();
        var posterHtml = await posterPage.Content.ReadAsStringAsync();
        Assert.Contains("Poster.pdf", posterHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Approval-Sheet.pdf", posterHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(".txt", posterHtml, StringComparison.Ordinal);
        Assert.Contains("Up", posterHtml, StringComparison.Ordinal);

        var blocked = await allowed.GetAsync("/technician/library/drafts");
        blocked.EnsureSuccessStatusCode();
        var blockedHtml = await blocked.Content.ReadAsStringAsync();
        Assert.Contains("That location is not in the poster library.", blockedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("draft-secret", blockedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, blockedHtml, StringComparison.OrdinalIgnoreCase);

        var poster = await allowed.GetAsync("/technician/files/POSTER-2031-000002/poster");
        poster.EnsureSuccessStatusCode();
        Assert.Equal(posterBytes, await poster.Content.ReadAsByteArrayAsync());
        Assert.Contains("Poster.pdf", poster.Content.Headers.ContentDisposition?.FileName, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, poster.Content.Headers.ToString(), StringComparison.OrdinalIgnoreCase);

        var inline = await allowed.GetAsync("/technician/files/POSTER-2031-000002/poster?inline=true");
        inline.EnsureSuccessStatusCode();
        Assert.Equal(posterBytes, await inline.Content.ReadAsByteArrayAsync());
        Assert.True(inline.Content.Headers.ContentDisposition is null || inline.Content.Headers.ContentDisposition.DispositionType == "inline");
        Assert.DoesNotContain(_root, inline.Content.Headers.ToString(), StringComparison.OrdinalIgnoreCase);

        var approval = await allowed.GetAsync("/technician/files/POSTER-2031-000002/approval?inline=true");
        approval.EnsureSuccessStatusCode();
        Assert.Equal("sheet-bytes"u8.ToArray(), await approval.Content.ReadAsByteArrayAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static PosterRequest Request(
        int departmentId,
        string posterId,
        string? itPerson,
        DateOnly? received,
        bool printed,
        DateOnly? dateOut,
        string? storagePath = null,
        string? approvalPath = null) => new()
    {
        PosterId = posterId,
        Name = "Queue Person",
        DepartmentId = departmentId,
        DepartmentName = "Workspace",
        Room = "12",
        Phone = "555-0190",
        Email = "harper@example.edu",
        DateIn = new DateTime(2026, 10, 4, 9, 0, 0),
        PosterFile = new PosterFile
        {
            OriginalFileName = "Poster.pdf",
            DetectedFormat = "PDF",
            PageCount = 1,
            Width = 24,
            Length = 36,
            StoragePath = storagePath ?? "WITHOUT-EVENT/2031/" + posterId + "/Poster.pdf"
        },
        ApprovalSheet = approvalPath is null ? null : new ApprovalSheet { FileName = "Approval-Sheet.pdf", StoragePath = approvalPath },
        PosterProcessing = new PosterProcessing
        {
            ITPerson = itPerson,
            Received = received,
            Printed = printed,
            DateOut = dateOut
        }
    };
}
