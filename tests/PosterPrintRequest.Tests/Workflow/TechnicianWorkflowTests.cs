using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Tests.Requests;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

[Collection("Workflow database")]
public sealed class TechnicianWorkflowTests : IDisposable
{
    private readonly WorkflowDatabaseFixture _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poster-workflow-" + Guid.NewGuid().ToString("N"));
    private readonly DraftFileStore _files;

    public TechnicianWorkflowTests(WorkflowDatabaseFixture database)
    {
        _database = database;
        Directory.CreateDirectory(_root);
        _files = new DraftFileStore(Options.Create(new SharedStorageOptions { RootPath = _root }));
    }

    [Fact]
    public async Task Technician_stages_follow_the_request_and_skip_lamination_when_it_was_not_requested()
    {
        var choices = await LoadChoicesAsync();
        var department = choices.Departments.First();
        var laminated = await AcceptAsync(department.Value, "", "Laminated Poster", lamination: true);
        var plain = await AcceptAsync(department.Value, "", "Plain Poster", lamination: false);

        await using var context = _database.CreateContext();
        var logger = new ListLogger<TechnicianWorkflow>();
        var workflow = new TechnicianWorkflow(context, logger);

        Assert.Equal("Mark the poster received before continuing.", (await workflow.MarkPrintedAsync(laminated.PosterId, null, CancellationToken.None)).Message);

        var received = new DateOnly(2026, 10, 6);
        var taken = await workflow.TakeAsync(laminated.PosterId, "Jordan Lee", received, "Queued", CancellationToken.None);
        Assert.True(taken.Completed);
        Assert.Equal("Mark the poster printed before continuing.", (await workflow.MarkLaminatedAsync(laminated.PosterId, null, CancellationToken.None)).Message);
        Assert.Equal("Mark the poster printed before continuing.", (await workflow.MarkNotifiedAsync(laminated.PosterId, null, CancellationToken.None)).Message);

        Assert.True((await workflow.MarkPrintedAsync(laminated.PosterId, "On the plotter", CancellationToken.None)).Completed);
        Assert.Equal("Mark lamination before continuing.", (await workflow.MarkNotifiedAsync(laminated.PosterId, null, CancellationToken.None)).Message);
        Assert.True((await workflow.MarkLaminatedAsync(laminated.PosterId, "Laminated", CancellationToken.None)).Completed);
        Assert.True((await workflow.MarkNotifiedAsync(laminated.PosterId, "Called", CancellationToken.None)).Completed);
        Assert.Equal("Enter the date out.", (await workflow.CompletePickupAsync(laminated.PosterId, null, "Ada Lovelace", null, CancellationToken.None)).Message);
        Assert.Equal("Enter who picked up the poster.", (await workflow.CompletePickupAsync(laminated.PosterId, new DateOnly(2026, 10, 7), " ", null, CancellationToken.None)).Message);
        Assert.True((await workflow.CompletePickupAsync(laminated.PosterId, new DateOnly(2026, 10, 7), "Ada Lovelace", "Picked up", CancellationToken.None)).Completed);
        Assert.Equal("Pickup is already complete.", (await workflow.CompletePickupAsync(laminated.PosterId, new DateOnly(2026, 10, 8), "Someone", null, CancellationToken.None)).Message);

        var saved = await context.PosterRequests
            .Include(request => request.PosterProcessing)
            .SingleAsync(request => request.PosterId == laminated.PosterId);
        Assert.Equal(laminated.Name, saved.Name);
        Assert.Equal(laminated.Email, saved.Email);
        Assert.Equal(laminated.DateIn, saved.DateIn);
        Assert.Equal(laminated.PosterId, saved.PosterId);
        Assert.Equal("Jordan Lee", saved.PosterProcessing.ITPerson);
        Assert.Equal(received, saved.PosterProcessing.Received);
        Assert.True(saved.PosterProcessing.Printed);
        Assert.True(saved.PosterProcessing.Laminated);
        Assert.True(saved.PosterProcessing.Notified);
        Assert.Equal(new DateOnly(2026, 10, 7), saved.PosterProcessing.DateOut);
        Assert.Equal("Ada Lovelace", saved.PosterProcessing.PickedUpBy);
        Assert.Equal("Picked up", saved.PosterProcessing.Comments);

        Assert.Equal("Lamination was not requested.", (await workflow.MarkLaminatedAsync(plain.PosterId, null, CancellationToken.None)).Message);
        Assert.True((await workflow.TakeAsync(plain.PosterId, "Jordan Lee", received, null, CancellationToken.None)).Completed);
        Assert.True((await workflow.MarkPrintedAsync(plain.PosterId, null, CancellationToken.None)).Completed);
        Assert.Equal("Lamination was not requested.", (await workflow.MarkLaminatedAsync(plain.PosterId, null, CancellationToken.None)).Message);
        Assert.True((await workflow.MarkNotifiedAsync(plain.PosterId, null, CancellationToken.None)).Completed);
        Assert.True((await workflow.CompletePickupAsync(plain.PosterId, new DateOnly(2026, 10, 7), "Grace Hopper", null, CancellationToken.None)).Completed);

        var plainSaved = await context.PosterRequests
            .Include(request => request.PosterProcessing)
            .SingleAsync(request => request.PosterId == plain.PosterId);
        Assert.False(plainSaved.PosterProcessing.Laminated);
        Assert.True(plainSaved.PosterProcessing.Notified);
        Assert.Equal(new DateOnly(2026, 10, 7), plainSaved.PosterProcessing.DateOut);
        Assert.Contains(logger.Messages, message => message.Contains(laminated.PosterId, StringComparison.Ordinal) && message.Contains("Pickup", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains(_root, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logger.Messages, message => message.Contains(laminated.Email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_unsafe_requester_name_does_not_leave_the_storage_root()
    {
        var choices = await LoadChoicesAsync();
        var department = choices.Departments.First().Value;
        var openEvent = choices.Reasons.Single(reason => !reason.RequiresMentor && !reason.RequiresApprovalSheet);
        var outside = Path.Combine(Directory.GetParent(_root)!.FullName, "outside");
        var existed = Directory.Exists(outside);
        var draft = await CreateDraftAsync(department, openEvent.Id.ToString(CultureInfo.InvariantCulture));
        draft.Name = @"..\secret";
        draft.Room = "1";
        draft.Phone = "555-0199";
        draft.Email = "secret@example.edu";

        await using var context = _database.CreateContext();
        var outcome = await SubmitAsync(context, draft);

        Assert.False(outcome.Saved);
        Assert.Equal("The request could not be accepted. Submit it again.", outcome.Message);
        Assert.Equal(existed, Directory.Exists(outside));
        Assert.Equal(0, await context.PosterRequests.CountAsync(request => request.Email == "secret@example.edu"));
    }

    [Fact]
    public async Task The_poster_id_sequence_stops_at_the_approved_width()
    {
        var choices = await LoadChoicesAsync();
        var departmentId = int.Parse(choices.Departments.First().Value, CultureInfo.InvariantCulture);
        var posterId = PosterIds.Format(DateTime.Now.Year, PosterIds.MaximumSequence);
        await using var context = _database.CreateContext();
        context.PosterRequests.Add(new PosterRequest
        {
            PosterId = posterId,
            Name = "Sequence Ceiling",
            DepartmentId = departmentId,
            DepartmentName = choices.Departments.First().Label,
            Room = "1",
            Phone = "555-0188",
            Email = "ceiling@example.edu",
            DateIn = DateTime.Now,
            PosterFile = new PosterFile
            {
                OriginalFileName = "Poster.pdf",
                DetectedFormat = "PDF",
                PageCount = 1,
                Width = 24,
                Length = 36,
                StoragePath = "archived/Poster.pdf"
            },
            PosterProcessing = new PosterProcessing()
        });
        await context.SaveChangesAsync();

        try
        {
            var draft = await CreateDraftAsync(departmentId.ToString(CultureInfo.InvariantCulture), "");
            draft.Name = "After Ceiling";
            draft.Room = "2";
            draft.Phone = "555-0189";
            draft.Email = "after-ceiling@example.edu";
            var outcome = await SubmitAsync(context, draft);
            Assert.False(outcome.Saved);
            Assert.Equal(0, await context.PosterRequests.CountAsync(request => request.Name == "After Ceiling"));
        }
        finally
        {
            var ceiling = await context.PosterRequests.SingleAsync(request => request.PosterId == posterId);
            context.Remove(ceiling);
            await context.SaveChangesAsync();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<PosterRequest> AcceptAsync(string departmentId, string reasonId, string name, bool lamination)
    {
        var draft = await CreateDraftAsync(departmentId, reasonId);
        draft.Name = name;
        draft.Room = "12";
        draft.Phone = "555-0120";
        draft.Email = name.Replace(' ', '-').ToLowerInvariant() + "@example.edu";
        draft.LaminationRequested = lamination;
        await using var context = _database.CreateContext();
        var outcome = await SubmitAsync(context, draft);
        Assert.True(outcome.Saved);
        return await context.PosterRequests.SingleAsync(request => request.PosterId == outcome.PosterId);
    }

    private async Task<RequesterChoices> LoadChoicesAsync()
    {
        await using var context = _database.CreateContext();
        await RequesterOptionExamples.EnsureAsync(context);
        return await new RequesterOptionCatalog(context).LoadAsync(CancellationToken.None);
    }

    private Task<SubmissionOutcome> SubmitAsync(PosterPrintRequest.Infrastructure.Persistence.PosterPrintRequestDbContext context, RequesterDraft draft) =>
        new RequesterSubmissionService(
            context,
            _files,
            new PosterPreflight(_files),
            new AcceptedStorage(Options.Create(new SharedStorageOptions { RootPath = _root }), _files),
            new ApprovalSheetCheck(_files),
            NullLogger<RequesterSubmissionService>.Instance).SubmitAsync(draft, CancellationToken.None);

    private async Task<RequesterDraft> CreateDraftAsync(string departmentId, string reasonId)
    {
        var poster = SamplePosters.Pdf(24, 36);
        var draft = new RequesterDraft
        {
            DraftId = DraftIds.Create(),
            DepartmentId = departmentId,
            ReasonId = reasonId
        };
        await using var stream = new MemoryStream(poster);
        var saved = await _files.SaveAsync(draft.DraftId, DraftFileRole.Poster, "Poster.pdf", stream, poster.Length, null, CancellationToken.None);
        Assert.True(saved.Saved);
        draft.Poster = new DraftFileState { OriginalFileName = "Poster.pdf", Size = poster.Length, Format = "PDF" };
        return draft;
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
