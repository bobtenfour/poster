using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

[Collection("Workflow database")]
public sealed class PrintFolderTests : IDisposable
{
    private readonly WorkflowDatabaseFixture _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poster-print-folder-" + Guid.NewGuid().ToString("N"));

    public PrintFolderTests(WorkflowDatabaseFixture database)
    {
        _database = database;
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Migration_moves_printed_and_unprinted_posters_without_a_second_print_state()
    {
        var departmentId = await DepartmentAsync();
        Assert.True(PrintFolderPaths.TryMapAcceptedFile("EVENTS/Example event, mentor and approval sheet required 2026/Ada Lovelace - POSTER-2026-000001/Poster.pdf", true, out var knownPoster));
        Assert.Equal("EVENTS/Example event, mentor and approval sheet required 2026 PRINTED/Ada Lovelace - POSTER-2026-000001/Poster.pdf", knownPoster);
        Assert.True(PrintFolderPaths.TryMapAcceptedFile("EVENTS/Example event, mentor and approval sheet required 2026/Ada Lovelace - POSTER-2026-000001/Approval-Sheet.pdf", true, out var knownSheet));
        Assert.Equal("EVENTS/Example event, mentor and approval sheet required 2026 PRINTED/Ada Lovelace - POSTER-2026-000001/Approval-Sheet.pdf", knownSheet);
        Assert.True(PrintFolderPaths.TryMapAcceptedFile("WITHOUT-EVENT/2026/Grace Hopper - POSTER-2026-000002/Poster.pdf", false, out var waitingFile));
        Assert.Equal("WITHOUT-EVENT TO BE PRINTED/2026/Grace Hopper - POSTER-2026-000002/Poster.pdf", waitingFile);
        Assert.True(PrintFolderPaths.TryMapAcceptedFile("WITHOUT-EVENT/2026/Grace Hopper - POSTER-2026-000002/Poster.pdf", true, out var printedWithout));
        Assert.Equal("WITHOUT-EVENT PRINTED/2026/Grace Hopper - POSTER-2026-000002/Poster.pdf", printedWithout);

        var printedDir = Path.Combine(_root, "EVENTS", "Example event, mentor and approval sheet required 2026", "Ada Lovelace - POSTER-2026-880001");
        var waitingDir = Path.Combine(_root, "WITHOUT-EVENT", "2026", "Grace Hopper - POSTER-2026-880002");
        var printedWithoutDir = Path.Combine(_root, "WITHOUT-EVENT", "2026", "Katherine Johnson - POSTER-2026-880003");
        Directory.CreateDirectory(printedDir);
        Directory.CreateDirectory(waitingDir);
        Directory.CreateDirectory(printedWithoutDir);
        var posterBytes = "poster-bytes"u8.ToArray();
        var sheetBytes = "sheet-bytes"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(printedDir, StorageNames.PosterPdf), posterBytes);
        await File.WriteAllBytesAsync(Path.Combine(printedDir, StorageNames.ApprovalSheet), sheetBytes);
        await File.WriteAllBytesAsync(Path.Combine(waitingDir, StorageNames.PosterPdf), posterBytes);
        await File.WriteAllBytesAsync(Path.Combine(printedWithoutDir, StorageNames.PosterPdf), posterBytes);

        var acceptedBefore = 0;
        await using (var context = _database.CreateContext())
        {
            var existing = await context.PosterRequests.Include(request => request.PosterFile).Include(request => request.PosterProcessing).ToListAsync();
            acceptedBefore = existing.Count(request => PrintFolderPaths.TryMapAcceptedFile(request.PosterFile.StoragePath, request.PosterProcessing.Printed, out _));
            context.PosterRequests.Add(Poster(departmentId, "POSTER-2026-880001", "Ada Lovelace", printed: true, "EVENTS/Example event, mentor and approval sheet required 2026/Ada Lovelace - POSTER-2026-880001/Poster.pdf", "EVENTS/Example event, mentor and approval sheet required 2026/Ada Lovelace - POSTER-2026-880001/Approval-Sheet.pdf", "Example event, mentor and approval sheet required"));
            context.PosterRequests.Add(Poster(departmentId, "POSTER-2026-880002", "Grace Hopper", printed: false, "WITHOUT-EVENT/2026/Grace Hopper - POSTER-2026-880002/Poster.pdf", null, null));
            context.PosterRequests.Add(Poster(departmentId, "POSTER-2026-880003", "Katherine Johnson", printed: true, "WITHOUT-EVENT/2026/Katherine Johnson - POSTER-2026-880003/Poster.pdf", null, null));
            context.PosterRequests.Add(Poster(departmentId, "POSTER-2026-880004", "Missing File", printed: false, "WITHOUT-EVENT/2026/Missing File - POSTER-2026-880004/Poster.pdf", null, null));
            await context.SaveChangesAsync();
        }

        await using var migrateContext = _database.CreateContext();
        var migration = new PrintFolderMigration(migrateContext, Storage(), NullLogger<PrintFolderMigration>.Instance);
        var first = await migration.MigrateAsync(CancellationToken.None);
        var second = await migration.MigrateAsync(CancellationToken.None);

        Assert.Equal(3, first.Moved);
        Assert.Equal(acceptedBefore + 1, first.Incomplete);
        Assert.Equal(0, second.Moved);
        Assert.Equal(acceptedBefore + 1, second.Incomplete);
        Assert.False(Directory.Exists(printedDir));
        Assert.False(Directory.Exists(waitingDir));
        Assert.False(Directory.Exists(printedWithoutDir));
        var printedDestination = Path.Combine(_root, "EVENTS", "Example event, mentor and approval sheet required 2026 PRINTED", "Ada Lovelace - POSTER-2026-880001");
        var waitingDestination = Path.Combine(_root, "WITHOUT-EVENT TO BE PRINTED", "2026", "Grace Hopper - POSTER-2026-880002");
        Assert.Equal(posterBytes, await File.ReadAllBytesAsync(Path.Combine(printedDestination, StorageNames.PosterPdf)));
        Assert.Equal(sheetBytes, await File.ReadAllBytesAsync(Path.Combine(printedDestination, StorageNames.ApprovalSheet)));
        Assert.Equal(posterBytes, await File.ReadAllBytesAsync(Path.Combine(waitingDestination, StorageNames.PosterPdf)));
        var printedWithoutDestination = Path.Combine(_root, "WITHOUT-EVENT PRINTED", "2026", "Katherine Johnson - POSTER-2026-880003");
        Assert.Equal(posterBytes, await File.ReadAllBytesAsync(Path.Combine(printedWithoutDestination, StorageNames.PosterPdf)));
        Assert.Empty(Directory.EnumerateFiles(printedDestination, "*.txt"));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(printedDir)!, "*", SearchOption.AllDirectories));

        await using var saved = _database.CreateContext();
        var known = await saved.PosterRequests.Include(request => request.PosterFile).Include(request => request.ApprovalSheet).Include(request => request.PosterProcessing).SingleAsync(request => request.PosterId == "POSTER-2026-880001");
        Assert.True(known.PosterProcessing.Printed);
        Assert.Equal("Ada Lovelace", known.Name);
        Assert.Equal("Example event, mentor and approval sheet required", known.ReasonName);
        Assert.Equal("EVENTS/Example event, mentor and approval sheet required 2026 PRINTED/Ada Lovelace - POSTER-2026-880001/Poster.pdf", known.PosterFile.StoragePath);
        Assert.Equal("EVENTS/Example event, mentor and approval sheet required 2026 PRINTED/Ada Lovelace - POSTER-2026-880001/Approval-Sheet.pdf", known.ApprovalSheet!.StoragePath);
        var waiting = await saved.PosterRequests.Include(request => request.PosterFile).Include(request => request.PosterProcessing).SingleAsync(request => request.PosterId == "POSTER-2026-880002");
        Assert.False(waiting.PosterProcessing.Printed);
        Assert.Null(waiting.ReasonId);
        Assert.Equal("WITHOUT-EVENT TO BE PRINTED/2026/Grace Hopper - POSTER-2026-880002/Poster.pdf", waiting.PosterFile.StoragePath);
        var printedWithoutRequest = await saved.PosterRequests.Include(request => request.PosterFile).Include(request => request.PosterProcessing).SingleAsync(request => request.PosterId == "POSTER-2026-880003");
        Assert.True(printedWithoutRequest.PosterProcessing.Printed);
        Assert.Null(printedWithoutRequest.ReasonId);
        Assert.Equal("WITHOUT-EVENT PRINTED/2026/Katherine Johnson - POSTER-2026-880003/Poster.pdf", printedWithoutRequest.PosterFile.StoragePath);
        var missing = await saved.PosterRequests.Include(request => request.PosterFile).Include(request => request.PosterProcessing).SingleAsync(request => request.PosterId == "POSTER-2026-880004");
        Assert.False(missing.PosterProcessing.Printed);
        Assert.Equal("WITHOUT-EVENT/2026/Missing File - POSTER-2026-880004/Poster.pdf", missing.PosterFile.StoragePath);
    }

    [Fact]
    public async Task Printed_transition_moves_the_poster_directory_and_restores_it_when_saving_fails()
    {
        var departmentId = await DepartmentAsync();
        var source = Path.Combine(_root, "EVENTS", "Open House 2026 TO BE PRINTED", "Ada Lovelace - POSTER-2026-880050");
        Directory.CreateDirectory(source);
        var posterBytes = "event-poster"u8.ToArray();
        var sheetBytes = "event-sheet"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(source, StorageNames.PosterPdf), posterBytes);
        await File.WriteAllBytesAsync(Path.Combine(source, StorageNames.ApprovalSheet), sheetBytes);
        const string posterPath = "EVENTS/Open House 2026 TO BE PRINTED/Ada Lovelace - POSTER-2026-880050/Poster.pdf";
        const string approvalPath = "EVENTS/Open House 2026 TO BE PRINTED/Ada Lovelace - POSTER-2026-880050/Approval-Sheet.pdf";
        await using (var context = _database.CreateContext())
        {
            context.PosterRequests.Add(Poster(departmentId, "POSTER-2026-880050", "Ada Lovelace", printed: false, posterPath, approvalPath, "Open House"));
            var received = context.PosterRequests.Local.Single().PosterProcessing;
            received.ITPerson = "Jordan Lee";
            received.Received = new DateOnly(2026, 10, 6);
            await context.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var workflow = new TechnicianWorkflow(context, NullLogger<TechnicianWorkflow>.Instance, Storage());
            Assert.True((await workflow.MarkPrintedAsync("POSTER-2026-880050", "On the plotter", CancellationToken.None)).Completed);
        }

        var destination = Path.Combine(_root, "EVENTS", "Open House 2026 PRINTED", "Ada Lovelace - POSTER-2026-880050");
        Assert.False(Directory.Exists(source));
        Assert.Equal(posterBytes, await File.ReadAllBytesAsync(Path.Combine(destination, StorageNames.PosterPdf)));
        Assert.Equal(sheetBytes, await File.ReadAllBytesAsync(Path.Combine(destination, StorageNames.ApprovalSheet)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(source)!));

        await using var saved = _database.CreateContext();
        var request = await saved.PosterRequests.Include(item => item.PosterFile).Include(item => item.ApprovalSheet).Include(item => item.PosterProcessing).SingleAsync(item => item.PosterId == "POSTER-2026-880050");
        Assert.True(request.PosterProcessing.Printed);
        Assert.Equal("Jordan Lee", request.PosterProcessing.ITPerson);
        Assert.Equal("Open House", request.ReasonName);
        Assert.Equal("EVENTS/Open House 2026 PRINTED/Ada Lovelace - POSTER-2026-880050/Poster.pdf", request.PosterFile.StoragePath);
        Assert.Equal("EVENTS/Open House 2026 PRINTED/Ada Lovelace - POSTER-2026-880050/Approval-Sheet.pdf", request.ApprovalSheet!.StoragePath);

        Directory.CreateDirectory(source);
        await File.WriteAllBytesAsync(Path.Combine(source, StorageNames.PosterPdf), posterBytes);
        await File.WriteAllBytesAsync(Path.Combine(source, StorageNames.ApprovalSheet), sheetBytes);
        request.PosterProcessing.Printed = false;
        request.PosterProcessing.Comments = null;
        request.PosterFile.StoragePath = posterPath;
        request.ApprovalSheet.StoragePath = approvalPath;
        await saved.SaveChangesAsync();
        Directory.Delete(destination, recursive: true);

        var blocking = Path.Combine(_root, "EVENTS", "Open House 2026 PRINTED", "Ada Lovelace - POSTER-2026-880050");
        Directory.CreateDirectory(blocking);
        await File.WriteAllBytesAsync(Path.Combine(blocking, StorageNames.PosterPdf), "other"u8.ToArray());
        await using (var context = _database.CreateContext())
        {
            var workflow = new TechnicianWorkflow(context, NullLogger<TechnicianWorkflow>.Instance, Storage());
            var blocked = await workflow.MarkPrintedAsync("POSTER-2026-880050", null, CancellationToken.None);
            Assert.False(blocked.Completed);
        }

        Assert.Equal(posterBytes, await File.ReadAllBytesAsync(Path.Combine(source, StorageNames.PosterPdf)));
        Assert.Equal("other"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(blocking, StorageNames.PosterPdf)));
        await using var stillWaiting = _database.CreateContext();
        var unchanged = await stillWaiting.PosterRequests.Include(item => item.PosterProcessing).Include(item => item.PosterFile).SingleAsync(item => item.PosterId == "POSTER-2026-880050");
        Assert.False(unchanged.PosterProcessing.Printed);
        Assert.Equal(posterPath, unchanged.PosterFile.StoragePath);

        Directory.Delete(blocking, recursive: true);
        var options = new DbContextOptionsBuilder<PosterPrintRequestDbContext>()
            .UseSqlServer(_database.ConnectionString)
            .AddInterceptors(new RejectSaveInterceptor())
            .Options;
        await using var failing = new PosterPrintRequestDbContext(options);
        var failingWorkflow = new TechnicianWorkflow(failing, NullLogger<TechnicianWorkflow>.Instance, Storage());
        var failedSave = await failingWorkflow.MarkPrintedAsync("POSTER-2026-880050", "Should not stick", CancellationToken.None);
        Assert.False(failedSave.Completed);
        Assert.Equal("The request could not be updated.", failedSave.Message);
        Assert.Equal(posterBytes, await File.ReadAllBytesAsync(Path.Combine(source, StorageNames.PosterPdf)));
        Assert.Equal(sheetBytes, await File.ReadAllBytesAsync(Path.Combine(source, StorageNames.ApprovalSheet)));
        Assert.False(Directory.Exists(destination));
        await using var restored = _database.CreateContext();
        var restoredRequest = await restored.PosterRequests.Include(item => item.PosterProcessing).Include(item => item.PosterFile).Include(item => item.ApprovalSheet).SingleAsync(item => item.PosterId == "POSTER-2026-880050");
        Assert.False(restoredRequest.PosterProcessing.Printed);
        Assert.Equal(posterPath, restoredRequest.PosterFile.StoragePath);
        Assert.Equal(approvalPath, restoredRequest.ApprovalSheet!.StoragePath);
        Assert.Null(restoredRequest.PosterProcessing.Comments);
    }

    [Fact]
    public async Task A_missing_to_be_printed_directory_does_not_mark_the_poster_printed()
    {
        var departmentId = await DepartmentAsync();
        await using var context = _database.CreateContext();
        var request = Poster(departmentId, "POSTER-2026-880060", "Grace Hopper", printed: false, "WITHOUT-EVENT TO BE PRINTED/2026/Grace Hopper - POSTER-2026-880060/Poster.pdf", null, null);
        request.PosterProcessing.Received = new DateOnly(2026, 10, 8);
        request.PosterProcessing.ITPerson = "Jordan Lee";
        context.PosterRequests.Add(request);
        await context.SaveChangesAsync();
        var workflow = new TechnicianWorkflow(context, NullLogger<TechnicianWorkflow>.Instance, Storage());

        var outcome = await workflow.MarkPrintedAsync("POSTER-2026-880060", null, CancellationToken.None);

        Assert.False(outcome.Completed);
        Assert.False(request.PosterProcessing.Printed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private AcceptedStorage Storage()
    {
        var options = Options.Create(new SharedStorageOptions { RootPath = _root });
        return new AcceptedStorage(options, new DraftFileStore(options));
    }

    private async Task<int> DepartmentAsync()
    {
        await using var context = _database.CreateContext();
        var department = new Department { Name = "Print folders " + Guid.NewGuid().ToString("N") };
        context.Departments.Add(department);
        await context.SaveChangesAsync();
        return department.DepartmentId;
    }

    private static PosterRequest Poster(
        int departmentId,
        string posterId,
        string name,
        bool printed,
        string posterPath,
        string? approvalPath,
        string? eventName) => new()
    {
        PosterId = posterId,
        Name = name,
        DepartmentId = departmentId,
        DepartmentName = "Print folders",
        Room = "1",
        Phone = "555-0100",
        Email = "print@example.edu",
        DateIn = new DateTime(2026, 10, 6, 15, 0, 0),
        ReasonName = eventName,
        PosterFile = new PosterFile
        {
            OriginalFileName = "Poster.pdf",
            DetectedFormat = "PDF",
            PageCount = 1,
            Width = 24,
            Length = 36,
            StoragePath = posterPath
        },
        ApprovalSheet = approvalPath is null ? null : new ApprovalSheet { FileName = StorageNames.ApprovalSheet, StoragePath = approvalPath },
        PosterProcessing = new PosterProcessing { Printed = printed }
    };

    private sealed class RejectSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => throw new DbUpdateException("The request could not be updated.");
    }
}
