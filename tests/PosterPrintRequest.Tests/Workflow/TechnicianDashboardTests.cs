using Microsoft.Extensions.Logging.Abstractions;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

[Collection("Workflow database")]
public sealed class TechnicianDashboardTests
{
    private readonly WorkflowDatabaseFixture _database;

    public TechnicianDashboardTests(WorkflowDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Statistics_ranking_and_queue_come_from_processing_records()
    {
        await using var beforeContext = _database.CreateContext();
        var beforeDashboard = new TechnicianDashboard(beforeContext);
        var before = await beforeDashboard.LoadAsync(2026, 10, CancellationToken.None);
        var beforeSeptember = await beforeDashboard.LoadAsync(2026, 9, CancellationToken.None);

        int departmentId;
        await using (var context = _database.CreateContext())
        {
            var department = new Department { Name = "Dashboard " + Guid.NewGuid().ToString("N") };
            context.Departments.Add(department);
            await context.SaveChangesAsync();
            departmentId = department.DepartmentId;
            Add(context, departmentId, "POSTER-2026-000810", "Morgan Hale", new DateOnly(2026, 10, 2), printed: false, laminated: false, notified: false, dateOut: null, lamination: false, new DateTime(2026, 10, 2, 8, 0, 0));
            Add(context, departmentId, "POSTER-2026-000811", "Morgan Hale", new DateOnly(2026, 10, 3), printed: true, laminated: false, notified: false, dateOut: null, lamination: false, new DateTime(2026, 10, 3, 8, 0, 0));
            Add(context, departmentId, "POSTER-2026-000812", "Morgan Hale", new DateOnly(2026, 10, 4), printed: true, laminated: true, notified: true, dateOut: null, lamination: true, new DateTime(2026, 10, 4, 8, 0, 0));
            Add(context, departmentId, "POSTER-2026-000813", "Avery Quinn", new DateOnly(2026, 10, 5), printed: true, laminated: false, notified: true, dateOut: new DateOnly(2026, 10, 6), lamination: false, new DateTime(2026, 10, 5, 8, 0, 0));
            Add(context, departmentId, "POSTER-2026-000814", "Avery Quinn", new DateOnly(2026, 10, 6), printed: true, laminated: false, notified: true, dateOut: new DateOnly(2026, 10, 7), lamination: false, new DateTime(2026, 10, 6, 8, 0, 0));
            Add(context, departmentId, "POSTER-2026-000815", "Riley Cho", new DateOnly(2026, 10, 7), printed: true, laminated: false, notified: true, dateOut: new DateOnly(2026, 10, 8), lamination: false, new DateTime(2026, 10, 7, 8, 0, 0));
            Add(context, departmentId, "POSTER-2026-000816", "Morgan Hale", new DateOnly(2026, 9, 15), printed: true, laminated: false, notified: true, dateOut: new DateOnly(2026, 9, 16), lamination: false, new DateTime(2026, 9, 15, 8, 0, 0));
            Add(context, departmentId, "POSTER-2026-000817", null, new DateOnly(2026, 10, 8), printed: true, laminated: false, notified: true, dateOut: new DateOnly(2026, 10, 9), lamination: false, new DateTime(2026, 10, 8, 8, 0, 0));
            Add(context, departmentId, "POSTER-2026-000818", null, received: null, printed: false, laminated: false, notified: false, dateOut: null, lamination: false, new DateTime(2026, 10, 1, 8, 0, 0));
            Add(context, departmentId, "POSTER-2025-000001", "Prior Year", new DateOnly(2025, 12, 1), printed: true, laminated: false, notified: true, dateOut: new DateOnly(2025, 12, 2), lamination: false, new DateTime(2025, 12, 1, 8, 0, 0));
            await context.SaveChangesAsync();
        }

        await using var afterContext = _database.CreateContext();
        var dashboard = new TechnicianDashboard(afterContext);
        var after = await dashboard.LoadAsync(2026, 10, CancellationToken.None);
        var september = await dashboard.LoadAsync(2026, 9, CancellationToken.None);
        var prior = await dashboard.LoadAsync(2025, 12, CancellationToken.None);

        Assert.Equal(before.PendingPrinting + 2, after.PendingPrinting);
        Assert.Equal(before.PrintedThisMonth + 6, after.PrintedThisMonth);
        Assert.Equal(before.PrintedThisYear + 7, after.PrintedThisYear);
        Assert.Equal(before.AwaitingPickup + 1, after.AwaitingPickup);
        Assert.Equal(before.TotalProcessed + 6, after.TotalProcessed);
        Assert.Equal("October 2026", after.PeriodLabel);
        Assert.Equal(beforeSeptember.PrintedThisMonth + 1, september.PrintedThisMonth);
        Assert.Equal(1, prior.Performance.Single(person => person.Technician == "Prior Year").PostersPrinted);
        Assert.DoesNotContain(after.Performance, person => person.Technician == "Prior Year");

        var morgan = after.Performance.Single(person => person.Technician == "Morgan Hale");
        var avery = after.Performance.Single(person => person.Technician == "Avery Quinn");
        var riley = after.Performance.Single(person => person.Technician == "Riley Cho");
        var unassigned = after.Performance.Single(person => person.Technician == TechnicianDashboard.Unassigned);
        Assert.Equal(2, morgan.PostersPrinted);
        Assert.Equal(2, avery.PostersPrinted);
        Assert.Equal(morgan.Rank, avery.Rank);
        Assert.Equal(1, riley.PostersPrinted);
        Assert.True(riley.Rank > morgan.Rank);
        Assert.Equal(1, unassigned.PostersPrinted);
        Assert.Equal(1, september.Performance.Single(person => person.Technician == "Morgan Hale").PostersPrinted);

        Assert.Equal("Receive", after.Queue.Single(item => item.PosterId == "POSTER-2026-000818").Stage);
        Assert.Equal("Print", after.Queue.Single(item => item.PosterId == "POSTER-2026-000810").Stage);
        Assert.Equal("Notify", after.Queue.Single(item => item.PosterId == "POSTER-2026-000811").Stage);
        var pickup = after.Queue.Single(item => item.PosterId == "POSTER-2026-000812");
        Assert.Equal("Pickup", pickup.Stage);
        Assert.True(pickup.LaminationRequested);
        Assert.DoesNotContain(after.Queue, item => item.PosterId == "POSTER-2026-000813");
        string[] openIds = ["POSTER-2026-000818", "POSTER-2026-000810", "POSTER-2026-000811", "POSTER-2026-000812"];
        Assert.Equal(openIds, after.Queue.Select(item => item.PosterId).Where(openIds.Contains).ToArray());

        await using var workflowContext = _database.CreateContext();
        var workflow = new TechnicianWorkflow(workflowContext, NullLogger<TechnicianWorkflow>.Instance);
        Assert.True((await workflow.MarkPrintedAsync("POSTER-2026-000810", "On the plotter", CancellationToken.None)).Completed);

        await using var laterContext = _database.CreateContext();
        var later = await new TechnicianDashboard(laterContext).LoadAsync(2026, 10, CancellationToken.None);
        Assert.Equal(after.PendingPrinting - 1, later.PendingPrinting);
        Assert.Equal(after.PrintedThisMonth + 1, later.PrintedThisMonth);
        Assert.Equal(after.PrintedThisYear + 1, later.PrintedThisYear);
        Assert.Equal(3, later.Performance.Single(person => person.Technician == "Morgan Hale").PostersPrinted);
        Assert.Equal("Notify", later.Queue.Single(item => item.PosterId == "POSTER-2026-000810").Stage);
    }

    private static void Add(
        PosterPrintRequest.Infrastructure.Persistence.PosterPrintRequestDbContext context,
        int departmentId,
        string posterId,
        string? itPerson,
        DateOnly? received,
        bool printed,
        bool laminated,
        bool notified,
        DateOnly? dateOut,
        bool lamination,
        DateTime dateIn)
    {
        context.PosterRequests.Add(new PosterRequest
        {
            PosterId = posterId,
            Name = "Queue Person",
            DepartmentId = departmentId,
            DepartmentName = "Department",
            Room = "4",
            Phone = "555-0188",
            Email = "queue@example.edu",
            DateIn = dateIn,
            LaminationRequested = lamination,
            PosterFile = new PosterFile
            {
                OriginalFileName = "Poster.pdf",
                DetectedFormat = "PDF",
                PageCount = 1,
                Width = 24,
                Length = 36,
                StoragePath = "WITHOUT-EVENT/2026/" + posterId + "/Poster.pdf"
            },
            PosterProcessing = new PosterProcessing
            {
                ITPerson = itPerson,
                Received = received,
                Printed = printed,
                Laminated = laminated,
                Notified = notified,
                DateOut = dateOut
            }
        });
    }
}
