using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Requests;

public sealed class RequesterSubmissionTests : IClassFixture<RequestDatabaseFixture>, IDisposable
{
    private readonly RequestDatabaseFixture _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poster-submit-" + Guid.NewGuid().ToString("N"));
    private readonly DraftFileStore _files;

    public RequesterSubmissionTests(RequestDatabaseFixture database)
    {
        _database = database;
        Directory.CreateDirectory(_root);
        _files = new DraftFileStore(Options.Create(new SharedStorageOptions { RootPath = _root }));
    }

    [Fact]
    public async Task Submit_stores_the_requester_poster_and_required_approval_sheet()
    {
        var choices = await LoadChoicesAsync();
        var mentorEvent = choices.Reasons.Single(reason => reason.RequiresMentor && reason.RequiresApprovalSheet);
        var department = choices.Departments.Single(option => option.Label == "Example Department");
        var draft = await CreateDraftAsync(department.Value, mentorEvent.Id.ToString(), includeApproval: true);
        draft.Name = "Ada Lovelace";
        draft.Mentor = "Grace Hopper";
        draft.Room = "214";
        draft.Phone = "555-0100";
        draft.Email = "ada@example.edu";
        draft.LaminationRequested = true;

        await using var context = _database.CreateContext();
        var outcome = await SubmitAsync(context, draft);
        var saved = await context.PosterRequests
            .Include(request => request.PosterFile)
            .Include(request => request.ApprovalSheet)
            .Include(request => request.PosterProcessing)
            .SingleAsync(request => request.Name == "Ada Lovelace");

        Assert.True(outcome.Saved);
        Assert.Equal(outcome.PosterId, saved.PosterId);
        Assert.True(PosterIds.IsPublic(saved.PosterId));
        Assert.StartsWith($"POSTER-{DateTime.Now.Year}-", saved.PosterId, StringComparison.Ordinal);
        Assert.NotEqual(saved.PosterRequestId.ToString(CultureInfo.InvariantCulture), saved.PosterId);
        Assert.True((DateTime.Now - saved.DateIn).Duration() < TimeSpan.FromMinutes(2));
        Assert.Equal("Grace Hopper", saved.Mentor);
        Assert.Equal(int.Parse(department.Value, CultureInfo.InvariantCulture), saved.DepartmentId);
        Assert.Equal(mentorEvent.Id, saved.ReasonId);
        Assert.True(saved.LaminationRequested);
        Assert.True(saved.ApprovalSheetUploaded);
        Assert.Equal("Poster.pdf", saved.PosterFile.OriginalFileName);
        Assert.Equal("PDF", saved.PosterFile.DetectedFormat);
        Assert.Equal(1, saved.PosterFile.PageCount);
        Assert.Equal(36m, saved.PosterFile.Width);
        Assert.Equal(72m, saved.PosterFile.Length);
        Assert.Equal("Sheet.pdf", saved.ApprovalSheet!.FileName);
        Assert.NotNull(saved.PosterProcessing);
        Assert.Null(saved.PosterProcessing.ITPerson);
        Assert.Null(saved.PosterProcessing.Received);
        Assert.False(saved.PosterProcessing.Printed);
        Assert.False(saved.PosterProcessing.Laminated);
        Assert.False(saved.PosterProcessing.Notified);
        var folder = $"EVENTS/Example event, mentor and approval sheet required {saved.DateIn.Year}/Ada Lovelace - {saved.PosterId}";
        Assert.Equal($"{folder}/Poster.pdf", saved.PosterFile.StoragePath);
        Assert.Equal($"{folder}/Approval-Sheet.pdf", saved.ApprovalSheet.StoragePath);
        Assert.DoesNotContain(_root, saved.PosterFile.StoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SamplePosters.Pdf(36, 72), await File.ReadAllBytesAsync(Full(saved.PosterFile.StoragePath)));
        Assert.Equal(SamplePosters.Pdf(8.5m, 11m), await File.ReadAllBytesAsync(Full(saved.ApprovalSheet.StoragePath)));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(Full(saved.PosterFile.StoragePath))!, "*.txt"));
        Assert.False(_files.Exists(draft.DraftId, DraftFileRole.Poster));
    }

    [Fact]
    public async Task Mentor_and_approval_sheet_follow_the_selected_event()
    {
        var choices = await LoadChoicesAsync();
        var openEvent = choices.Reasons.Single(reason => !reason.RequiresMentor && !reason.RequiresApprovalSheet);
        var mentorEvent = choices.Reasons.Single(reason => reason.RequiresMentor && !reason.RequiresApprovalSheet);
        var department = choices.Departments.First().Value;

        var missingMentor = await CreateDraftAsync(department, mentorEvent.Id.ToString(), includeApproval: false);
        missingMentor.Name = "No Mentor";
        missingMentor.Room = "1";
        missingMentor.Phone = "555-0101";
        missingMentor.Email = "mentor@example.edu";
        await using (var context = _database.CreateContext())
        {
            var outcome = await SubmitAsync(context, missingMentor);
            Assert.False(outcome.Saved);
            Assert.Equal("Enter the mentor for this event.", outcome.FieldErrors["mentor"]);
        }

        var optional = await CreateDraftAsync(department, openEvent.Id.ToString(), includeApproval: true);
        optional.Name = "Open Event";
        optional.Room = "2";
        optional.Phone = "555-0102";
        optional.Email = "open@example.edu";
        await using var savedContext = _database.CreateContext();
        var savedOutcome = await SubmitAsync(savedContext, optional);

        Assert.True(savedOutcome.Saved);
        var saved = await savedContext.PosterRequests
            .Include(request => request.ApprovalSheet)
            .SingleAsync(request => request.Name == "Open Event");
        Assert.Null(saved.Mentor);
        Assert.False(saved.ApprovalSheetUploaded);
        Assert.Null(saved.ApprovalSheet);
    }

    [Fact]
    public async Task Horizontal_poster_is_stored_with_upright_dimensions_and_original_bytes()
    {
        var choices = await LoadChoicesAsync();
        var department = choices.Departments.First().Value;
        var poster = SamplePosters.Pdf(72, 36);
        var draft = await CreateDraftAsync(department, "", includeApproval: false, poster: poster, posterName: "Landscape.pdf");
        draft.Name = "Landscape";
        draft.Room = "3";
        draft.Phone = "555-0103";
        draft.Email = "landscape@example.edu";

        await using var context = _database.CreateContext();
        var outcome = await SubmitAsync(context, draft);

        Assert.True(outcome.Saved);
        var saved = await context.PosterRequests
            .Include(request => request.PosterFile)
            .SingleAsync(request => request.Name == "Landscape");
        Assert.Equal(36m, saved.PosterFile.Width);
        Assert.Equal(72m, saved.PosterFile.Length);
        var year = saved.DateIn.ToString("yyyy", CultureInfo.InvariantCulture);
        Assert.Equal($"WITHOUT-EVENT/{year}/Landscape - {saved.PosterId}/Poster.pdf", saved.PosterFile.StoragePath);
        Assert.Equal(poster, await File.ReadAllBytesAsync(Full(saved.PosterFile.StoragePath)));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(Full(saved.PosterFile.StoragePath))!, "*.txt"));
    }

    [Fact]
    public async Task PowerPoint_content_is_stored_even_when_the_selected_name_ends_in_pdf()
    {
        var choices = await LoadChoicesAsync();
        var department = choices.Departments.First().Value;
        var poster = SamplePosters.Pptx(24, 36);
        var draft = await CreateDraftAsync(department, "", includeApproval: false, poster: poster, posterName: "Poster.pdf");
        draft.Name = "Content Wins";
        draft.Room = "4";
        draft.Phone = "555-0104";
        draft.Email = "content@example.edu";

        await using var context = _database.CreateContext();
        var outcome = await SubmitAsync(context, draft);

        Assert.True(outcome.Saved);
        var saved = await context.PosterRequests
            .Include(request => request.PosterFile)
            .SingleAsync(request => request.Name == "Content Wins");
        Assert.Equal("PPTX", saved.PosterFile.DetectedFormat);
        Assert.Equal(1, saved.PosterFile.PageCount);
        Assert.Equal(24m, saved.PosterFile.Width);
        Assert.Equal(36m, saved.PosterFile.Length);
        Assert.EndsWith("/Poster.pptx", saved.PosterFile.StoragePath, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, saved.PosterFile.StoragePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(poster, await File.ReadAllBytesAsync(Full(saved.PosterFile.StoragePath)));
    }

    [Fact]
    public async Task A_two_page_poster_is_rejected_and_the_draft_remains()
    {
        var choices = await LoadChoicesAsync();
        var department = choices.Departments.First().Value;
        var poster = SamplePosters.Pages([(36m, 72m, 0), (36m, 72m, 0)]);
        var draft = await CreateDraftAsync(department, "", includeApproval: false, poster: poster);
        draft.Name = "Two Pages";
        draft.Mentor = "Kept";
        draft.Room = "5";
        draft.Phone = "555-0105";
        draft.Email = "pages@example.edu";

        await using var context = _database.CreateContext();
        var outcome = await SubmitAsync(context, draft);

        Assert.False(outcome.Saved);
        Assert.Equal(
            "The PDF has 2 pages. A poster must contain exactly one page. Correct the file and upload it again.",
            outcome.FieldErrors["poster"]);
        Assert.True(_files.Exists(draft.DraftId, DraftFileRole.Poster));
        Assert.Equal(poster, await File.ReadAllBytesAsync(_files.PosterPath(draft.DraftId)!));
        Assert.Equal(0, await context.PosterRequests.CountAsync(request => request.Name == "Two Pages"));
    }

    [Fact]
    public async Task A_non_pdf_approval_sheet_is_rejected_and_a_multi_page_sheet_is_accepted()
    {
        var choices = await LoadChoicesAsync();
        var approvalEvent = choices.Reasons.Single(reason => !reason.RequiresMentor && reason.RequiresApprovalSheet);
        var department = choices.Departments.First().Value;

        var invalid = await CreateDraftAsync(department, approvalEvent.Id.ToString(), includeApproval: false);
        invalid.Name = "Bad Sheet";
        invalid.Room = "8";
        invalid.Phone = "555-0108";
        invalid.Email = "sheet@example.edu";
        var badSheet = "sheet-bytes"u8.ToArray();
        await SaveBytesAsync(invalid.DraftId, DraftFileRole.ApprovalSheet, "Sheet.pdf", badSheet);
        invalid.ApprovalSheet = new DraftFileState { OriginalFileName = "Sheet.pdf", Size = badSheet.Length, Format = "PDF" };

        await using (var context = _database.CreateContext())
        {
            var outcome = await SubmitAsync(context, invalid);
            Assert.False(outcome.Saved);
            Assert.Equal(ApprovalSheetRules.MustBePdf, outcome.FieldErrors["approval"]);
            Assert.Equal(0, await context.PosterRequests.CountAsync(request => request.Name == "Bad Sheet"));
            Assert.True(_files.Exists(invalid.DraftId, DraftFileRole.ApprovalSheet));
        }

        var pages = SamplePosters.Pages([(8.5m, 11m, 0), (8.5m, 11m, 0)]);
        var valid = await CreateDraftAsync(department, approvalEvent.Id.ToString(), includeApproval: false);
        valid.Name = "Multi Sheet";
        valid.Room = "9";
        valid.Phone = "555-0109";
        valid.Email = "multi@example.edu";
        await SaveBytesAsync(valid.DraftId, DraftFileRole.ApprovalSheet, "Notes.pdf", pages);
        valid.ApprovalSheet = new DraftFileState { OriginalFileName = "Notes.pdf", Size = pages.Length, Format = "PDF" };

        await using var savedContext = _database.CreateContext();
        var savedOutcome = await SubmitAsync(savedContext, valid);
        Assert.True(savedOutcome.Saved);
        var saved = await savedContext.PosterRequests
            .Include(request => request.ApprovalSheet)
            .SingleAsync(request => request.Name == "Multi Sheet");
        Assert.True(saved.ApprovalSheetUploaded);
        Assert.Equal(pages, await File.ReadAllBytesAsync(Full(saved.ApprovalSheet!.StoragePath)));
    }

    [Fact]
    public async Task Acceptance_assigns_the_next_poster_id_and_logs_without_a_storage_path()
    {
        var choices = await LoadChoicesAsync();
        var department = choices.Departments.First().Value;
        var first = await CreateDraftAsync(department, "", includeApproval: false);
        first.Name = "First Sequence";
        first.Room = "10";
        first.Phone = "555-0110";
        first.Email = "first@example.edu";
        var second = await CreateDraftAsync(department, "", includeApproval: false);
        second.Name = "Second Sequence";
        second.Room = "11";
        second.Phone = "555-0111";
        second.Email = "second@example.edu";
        var logger = new ListLogger<RequesterSubmissionService>();

        await using var context = _database.CreateContext();
        var firstOutcome = await SubmitAsync(context, first, logger);
        var secondOutcome = await SubmitAsync(context, second, logger);

        Assert.True(PosterIds.TryParse(firstOutcome.PosterId, out var firstYear, out var firstSequence));
        Assert.True(PosterIds.TryParse(secondOutcome.PosterId, out var secondYear, out var secondSequence));
        Assert.Equal(DateTime.Now.Year, firstYear);
        Assert.Equal(firstYear, secondYear);
        Assert.Equal(firstSequence + 1, secondSequence);
        Assert.Contains(logger.Messages, message => message.Contains(firstOutcome.PosterId!, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains(_root, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("first@example.edu", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<RequesterChoices> LoadChoicesAsync()
    {
        await using var context = _database.CreateContext();
        return await new RequesterOptionCatalog(context).LoadAsync(CancellationToken.None);
    }

    private Task<SubmissionOutcome> SubmitAsync(PosterPrintRequest.Infrastructure.Persistence.PosterPrintRequestDbContext context, RequesterDraft draft) =>
        SubmitAsync(context, draft, NullLogger<RequesterSubmissionService>.Instance);

    private Task<SubmissionOutcome> SubmitAsync(
        PosterPrintRequest.Infrastructure.Persistence.PosterPrintRequestDbContext context,
        RequesterDraft draft,
        ILogger<RequesterSubmissionService> logger) =>
        new RequesterSubmissionService(
            context,
            _files,
            new PosterPreflight(_files),
            new AcceptedStorage(Options.Create(new SharedStorageOptions { RootPath = _root }), _files),
            new ApprovalSheetCheck(_files),
            logger).SubmitAsync(draft, CancellationToken.None);

    private string Full(string relative) =>
        Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

    private Task<RequesterDraft> CreateDraftAsync(string departmentId, string reasonId, bool includeApproval) =>
        CreateDraftAsync(departmentId, reasonId, includeApproval, SamplePosters.Pdf(36, 72), "Poster.pdf");

    private async Task<RequesterDraft> CreateDraftAsync(
        string departmentId,
        string reasonId,
        bool includeApproval,
        byte[] poster,
        string posterName = "Poster.pdf")
    {
        var draft = new RequesterDraft
        {
            DraftId = DraftIds.Create(),
            DepartmentId = departmentId,
            ReasonId = reasonId
        };
        await SaveBytesAsync(draft.DraftId, DraftFileRole.Poster, posterName, poster);
        draft.Poster = new DraftFileState
        {
            OriginalFileName = posterName,
            Size = poster.Length,
            Format = "PDF"
        };
        if (includeApproval)
        {
            var sheet = SamplePosters.Pdf(8.5m, 11m);
            await SaveBytesAsync(draft.DraftId, DraftFileRole.ApprovalSheet, "Sheet.pdf", sheet);
            draft.ApprovalSheet = new DraftFileState
            {
                OriginalFileName = "Sheet.pdf",
                Size = sheet.Length,
                Format = "PDF"
            };
        }

        return draft;
    }

    private async Task SaveBytesAsync(string draftId, DraftFileRole role, string fileName, byte[] bytes)
    {
        await using var stream = new MemoryStream(bytes);
        var saved = await _files.SaveAsync(draftId, role, fileName, stream, bytes.Length, null, CancellationToken.None);
        Assert.True(saved.Saved);
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
