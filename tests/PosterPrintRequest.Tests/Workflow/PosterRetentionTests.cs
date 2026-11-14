using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

[Collection("Workflow database")]
public sealed class PosterRetentionTests : IDisposable
{
    private readonly WorkflowDatabaseFixture _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poster-retention-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTime _time = new() { Now = new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero) };
    private readonly PosterRetentionSchedule _schedule = new();
    private readonly ListLogger _logger = new();

    public PosterRetentionTests(WorkflowDatabaseFixture database)
    {
        _database = database;
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Confirmed_deletion_moves_the_complete_directory_and_restore_returns_it()
    {
        var posterId = PosterIds.Format(2026, 810001);
        var relative = await StorePosterAsync(posterId, includeExtraFile: true);
        await using var context = _database.CreateContext();
        var retention = Create(context);
        var technician = Technician();

        var deleted = await retention.ConfirmDeletionAsync(posterId, technician, CancellationToken.None);
        Assert.True(deleted.Completed);
        Assert.False(Directory.Exists(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar))));
        var retained = Path.Combine(_root, PosterRetentionPaths.FolderName, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(Path.Combine(retained, StorageNames.PosterPdf)));
        Assert.True(File.Exists(Path.Combine(retained, StorageNames.ApprovalSheet)));
        Assert.Equal("keep-me", await File.ReadAllTextAsync(Path.Combine(retained, "notes.txt")));
        Assert.True((File.GetAttributes(Path.Combine(_root, PosterRetentionPaths.FolderName)) & FileAttributes.Hidden) != 0);

        var saved = await context.PosterRequests.Include(request => request.PosterFile).Include(request => request.ApprovalSheet)
            .SingleAsync(request => request.PosterId == posterId);
        Assert.Equal(_time.Now.UtcDateTime, saved.TemporaryDeletionAt);
        Assert.Equal(relative, saved.OriginalDirectory);
        Assert.StartsWith(PosterRetentionPaths.FolderName + "/", saved.PosterFile.StoragePath, StringComparison.Ordinal);
        Assert.StartsWith(PosterRetentionPaths.FolderName + "/", saved.ApprovalSheet!.StoragePath, StringComparison.Ordinal);

        var library = new TechnicianLibrary(TestStorage.Access(_root));
        var parent = relative[..relative.LastIndexOf('/')];
        var listing = library.List(parent, await retention.ListRetainedAsync(CancellationToken.None));
        var entry = Assert.Single(listing!.Entries, item => item.PosterId == posterId);
        Assert.Equal(Path.GetFileName(relative.Replace('/', Path.DirectorySeparatorChar)), entry.Name);
        Assert.True(entry.CanRestore);
        Assert.False(entry.CanDelete);
        Assert.Null(library.List(PosterRetentionPaths.FolderName));

        _time.Now = _time.Now.AddHours(47);
        var restored = await retention.RestoreAsync(posterId, technician, CancellationToken.None);
        Assert.True(restored.Completed);
        Assert.Equal("poster", await File.ReadAllTextAsync(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar), StorageNames.PosterPdf)));
        Assert.Equal("sheet", await File.ReadAllTextAsync(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar), StorageNames.ApprovalSheet)));
        Assert.Equal("keep-me", await File.ReadAllTextAsync(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar), "notes.txt")));
        Assert.False(Directory.Exists(retained));
        var after = await context.PosterRequests.SingleAsync(request => request.PosterId == posterId);
        Assert.Null(after.TemporaryDeletionAt);
        Assert.Null(after.OriginalDirectory);
        Assert.Equal(posterId, after.PosterId);
        Assert.Equal("Ada Lovelace", after.Name);

        var audit = await context.PosterRetentionAudits.Where(item => item.PosterId == posterId).OrderBy(item => item.PosterRetentionAuditId).ToListAsync();
        Assert.Equal([PosterRetentionAction.TemporaryDeletion, PosterRetentionAction.Restoration], audit.Select(item => item.Action).ToArray());
        Assert.All(audit, item => Assert.Equal(relative, item.OriginalDirectory));
        Assert.All(audit, item => Assert.Equal(new Guid("22222222-2222-2222-2222-000000000042"), item.ActorObjectId));
        Assert.DoesNotContain(_logger.Messages, message => message.Contains(_root, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_logger.Messages, message => message.Contains("Ada Lovelace", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancellation_is_not_a_storage_operation_and_unauthorized_callers_are_refused()
    {
        var posterId = PosterIds.Format(2026, 810002);
        var relative = await StorePosterAsync(posterId, includeExtraFile: false);
        await using var context = _database.CreateContext();
        var retention = Create(context);
        var denied = await retention.ConfirmDeletionAsync(posterId, Requester(), CancellationToken.None);
        Assert.False(denied.Completed);
        Assert.Equal(PosterRetentionMessages.UnauthorizedDelete, denied.Message);
        Assert.True(File.Exists(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar), StorageNames.PosterPdf)));
        Assert.Empty(await context.PosterRetentionAudits.Where(item => item.PosterId == posterId).ToListAsync());
    }

    [Fact]
    public async Task Retention_expires_exactly_at_48_hours_including_after_downtime()
    {
        var posterId = PosterIds.Format(2026, 810003);
        var relative = await StorePosterAsync(posterId, includeExtraFile: true);
        await using var context = _database.CreateContext();
        var retention = Create(context);
        Assert.True((await retention.ConfirmDeletionAsync(posterId, Technician(), CancellationToken.None)).Completed);
        Assert.Equal(TimeSpan.FromHours(1), await retention.DelayUntilNextExpirationAsync(CancellationToken.None));

        _time.Now = _time.Now.AddHours(47.5);
        Assert.Equal(TimeSpan.FromMinutes(30), await retention.DelayUntilNextExpirationAsync(CancellationToken.None));
        Assert.True((await retention.RestoreAsync(posterId, Technician(), CancellationToken.None)).Completed);
        Assert.True((await retention.ConfirmDeletionAsync(posterId, Technician(), CancellationToken.None)).Completed);

        _time.Now = _time.Now.AddHours(48);
        var ended = await retention.RestoreAsync(posterId, Technician(), CancellationToken.None);
        Assert.False(ended.Completed);
        Assert.Equal(PosterRetentionMessages.Ended, ended.Message);
        var retained = Path.Combine(_root, PosterRetentionPaths.FolderName, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.False(Directory.Exists(retained));
        var expired = await context.PosterRequests.SingleAsync(request => request.PosterId == posterId);
        Assert.NotNull(expired.PermanentlyDeletedAt);
        Assert.Equal(relative, expired.OriginalDirectory);
        Assert.DoesNotContain(
            await retention.ListRetainedAsync(CancellationToken.None),
            item => item.PosterId == posterId);

        var secondId = PosterIds.Format(2026, 810004);
        var second = await StorePosterAsync(secondId, includeExtraFile: false);
        Assert.True((await retention.ConfirmDeletionAsync(secondId, Technician(), CancellationToken.None)).Completed);
        _time.Now = _time.Now.AddHours(48);
        var services = new ServiceCollection();
        services.AddSingleton<IPosterRetention>(retention);
        await using var provider = services.BuildServiceProvider();
        var expiration = new PosterRetentionExpiration(provider.GetRequiredService<IServiceScopeFactory>(), _schedule, NullLogger<PosterRetentionExpiration>.Instance);
        await expiration.StartAsync(CancellationToken.None);
        await expiration.StopAsync(CancellationToken.None);
        Assert.False(Directory.Exists(Path.Combine(_root, PosterRetentionPaths.FolderName, second.Replace('/', Path.DirectorySeparatorChar))));
        var down = await context.PosterRequests.SingleAsync(request => request.PosterId == secondId);
        Assert.NotNull(down.PermanentlyDeletedAt);
        Assert.Contains(
            await context.PosterRetentionAudits.Where(item => item.PosterId == secondId).Select(item => item.Action).ToListAsync(),
            action => action == PosterRetentionAction.PermanentExpiration);
    }

    [Fact]
    public async Task Destination_conflicts_and_storage_failures_leave_the_poster_in_place()
    {
        var posterId = PosterIds.Format(2026, 810005);
        var relative = await StorePosterAsync(posterId, includeExtraFile: false);
        var destination = Path.Combine(_root, PosterRetentionPaths.FolderName, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(destination, "occupied.txt"), "occupied");

        await using var context = _database.CreateContext();
        var retention = Create(context);
        var conflict = await retention.ConfirmDeletionAsync(posterId, Technician(), CancellationToken.None);
        Assert.False(conflict.Completed);
        Assert.Equal(PosterRetentionMessages.DeleteConflict, conflict.Message);
        Assert.True(File.Exists(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar), StorageNames.PosterPdf)));
        Assert.Equal("occupied", await File.ReadAllTextAsync(Path.Combine(destination, "occupied.txt")));

        Directory.Delete(destination, recursive: true);
        Assert.True((await retention.ConfirmDeletionAsync(posterId, Technician(), CancellationToken.None)).Completed);
        Directory.CreateDirectory(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
        await File.WriteAllTextAsync(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar), "new.txt"), "new");
        var restoreConflict = await retention.RestoreAsync(posterId, Technician(), CancellationToken.None);
        Assert.False(restoreConflict.Completed);
        Assert.Equal(PosterRetentionMessages.RestoreConflict, restoreConflict.Message);
        Assert.True(File.Exists(Path.Combine(destination, StorageNames.PosterPdf)));
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar), "new.txt")));

        var failingId = PosterIds.Format(2026, 810006);
        await StorePosterAsync(failingId, includeExtraFile: false);
        var failing = new PosterRetention(
            context,
            new ThrowingMove(TestStorage.Access(_root)),
            new TechnicianAuthorization(),
            new ClaimsActorSource(),
            _time,
            _schedule,
            _logger);
        var failed = await failing.ConfirmDeletionAsync(failingId, Technician(), CancellationToken.None);
        Assert.False(failed.Completed);
        Assert.Equal(PosterRetentionMessages.DeleteFailed, failed.Message);
        var row = await context.PosterRequests.SingleAsync(request => request.PosterId == failingId);
        Assert.Null(row.TemporaryDeletionAt);
    }

    [Fact]
    public async Task Concurrent_deletion_moves_the_directory_once()
    {
        var posterId = PosterIds.Format(2026, 810007);
        var relative = await StorePosterAsync(posterId, includeExtraFile: false);
        await using var firstContext = _database.CreateContext();
        await using var secondContext = _database.CreateContext();
        var first = Create(firstContext);
        var second = Create(secondContext);
        var results = await Task.WhenAll(
            first.ConfirmDeletionAsync(posterId, Technician(), CancellationToken.None),
            second.ConfirmDeletionAsync(posterId, Technician(), CancellationToken.None));
        Assert.Equal(1, results.Count(result => result.Completed));
        Assert.False(Directory.Exists(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(Directory.Exists(Path.Combine(_root, PosterRetentionPaths.FolderName, relative.Replace('/', Path.DirectorySeparatorChar))));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            var folder = Path.Combine(_root, PosterRetentionPaths.FolderName);
            if (Directory.Exists(folder))
            {
                File.SetAttributes(folder, FileAttributes.Directory);
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    private PosterRetention Create(PosterPrintRequest.Infrastructure.Persistence.PosterPrintRequestDbContext context) =>
        new(context, TestStorage.Access(_root), new TechnicianAuthorization(), new ClaimsActorSource(), _time, _schedule, _logger);

    private async Task<string> StorePosterAsync(string posterId, bool includeExtraFile)
    {
        var folder = StorageNames.PosterFolder("Ada Lovelace", posterId)!;
        var relative = PrintFolderPaths.WithoutEventToBePrinted + "/2026/" + folder;
        var physical = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(physical);
        await File.WriteAllTextAsync(Path.Combine(physical, StorageNames.PosterPdf), "poster");
        await File.WriteAllTextAsync(Path.Combine(physical, StorageNames.ApprovalSheet), "sheet");
        if (includeExtraFile)
        {
            await File.WriteAllTextAsync(Path.Combine(physical, "notes.txt"), "keep-me");
        }

        await using var context = _database.CreateContext();
        var department = new Department { Name = "Retention " + posterId };
        context.Departments.Add(department);
        context.PosterRequests.Add(new PosterRequest
        {
            PosterId = posterId,
            Name = "Ada Lovelace",
            Department = department,
            DepartmentName = department.Name,
            Room = "214",
            Phone = "555-0100",
            Email = "ada@example.edu",
            DateIn = new DateTime(2026, 11, 1, 9, 0, 0),
            PosterFile = new PosterFile
            {
                OriginalFileName = "Poster.pdf",
                DetectedFormat = "PDF",
                PageCount = 1,
                Width = 36,
                Length = 48,
                StoragePath = relative + "/" + StorageNames.PosterPdf
            },
            ApprovalSheet = new ApprovalSheet
            {
                FileName = StorageNames.ApprovalSheet,
                StoragePath = relative + "/" + StorageNames.ApprovalSheet
            },
            ApprovalSheetUploaded = true,
            PosterProcessing = new PosterProcessing()
        });
        await context.SaveChangesAsync();
        return relative;
    }

    private static ClaimsPrincipal Technician() => Principal(PosterRoles.Technician);

    private static ClaimsPrincipal Requester() => Principal(PosterRoles.Requester);

    private static ClaimsPrincipal Principal(string role)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(PosterClaims.Roles, role),
                new Claim(PosterClaims.ObjectId, "22222222-2222-2222-2222-000000000042")
            ],
            "Test",
            PosterClaims.Name,
            PosterClaims.Roles);
        return new ClaimsPrincipal(identity);
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ListLogger : ILogger<PosterRetention>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class TechnicianAuthorization : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            AuthorizeAsync(user, resource, PosterAccess.OperatorPolicy);

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            var allowed = policyName == PosterAccess.OperatorPolicy
                && user.Identity?.IsAuthenticated == true
                && user.IsInRole(PosterRoles.Technician);
            return Task.FromResult(allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        }
    }

    private sealed class ThrowingMove : ISharedStorageAccess
    {
        private readonly ISharedStorageAccess _inner;

        public ThrowingMove(ISharedStorageAccess inner) => _inner = inner;

        public bool DirectoryExists(string relativePath) => _inner.DirectoryExists(relativePath);

        public bool FileExists(string relativePath) => _inner.FileExists(relativePath);

        public void CreateDirectory(string relativePath) => _inner.CreateDirectory(relativePath);

        public IReadOnlyList<StorageEntry> ListEntries(string relativePath) => _inner.ListEntries(relativePath);

        public Stream OpenRead(string relativePath) => _inner.OpenRead(relativePath);

        public Stream OpenWrite(string relativePath) => _inner.OpenWrite(relativePath);

        public void CopyFile(string sourceRelative, string destinationRelative) => _inner.CopyFile(sourceRelative, destinationRelative);

        public void MoveDirectory(string sourceRelative, string destinationRelative) => throw new StorageFailureException("MoveDirectory");

        public void DeleteFile(string relativePath) => _inner.DeleteFile(relativePath);

        public void DeleteDirectory(string relativePath) => _inner.DeleteDirectory(relativePath);

        public void HideDirectory(string relativePath) => _inner.HideDirectory(relativePath);

        public long FileLength(string relativePath) => _inner.FileLength(relativePath);

        public bool IsReparsePoint(string relativePath) => _inner.IsReparsePoint(relativePath);
    }
}
