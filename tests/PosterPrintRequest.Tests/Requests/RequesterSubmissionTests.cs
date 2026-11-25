using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Tests.Hosting;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Requests;

public sealed class RequesterSubmissionTests : IClassFixture<RequestDatabaseFixture>, IDisposable
{
    private readonly RequestDatabaseFixture _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poster-submit-" + Guid.NewGuid().ToString("N"));
    private readonly DraftFileStore _files;

    // Authentication Contract section 3.9: each test's requester name and email are the sign-in values,
    // kept per draft here because the draft itself no longer carries them.
    private readonly Dictionary<string, string> _names = new();
    private readonly Dictionary<string, string> _emails = new();

    public RequesterSubmissionTests(RequestDatabaseFixture database)
    {
        _database = database;
        Directory.CreateDirectory(_root);
        _files = new DraftFileStore(TestStorage.Access(_root));
    }

    [Fact]
    public async Task Submit_stores_the_requester_poster_and_required_approval_sheet()
    {
        var choices = await LoadChoicesAsync();
        var mentorEvent = choices.Reasons.Single(reason => reason.RequiresMentor && reason.RequiresApprovalSheet);
        var department = choices.Departments.Single(option => option.Label == "Example Department");
        var draft = await CreateDraftAsync(department.Value, mentorEvent.Id.ToString(), includeApproval: true);
        SetName(draft, "Ada Lovelace");
        draft.Mentor = "Grace Hopper";
        draft.Room = "214";
        draft.Phone = "555-0100";
        SetEmail(draft, "ada@example.edu");
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
        Assert.Equal(InstitutionalTestIdentities.RequesterOid, saved.SubmittedByObjectId);
        Assert.Equal(InstitutionalTestIdentities.Requester.PreferredUsername, saved.SubmittedByAccountName);
        Assert.Null(saved.SubmittedBySecurityIdentifier);
        Assert.Null(saved.PosterProcessing.ProcessedByObjectId);
        Assert.Null(saved.PosterProcessing.ProcessedByAccountName);
        Assert.Null(saved.PosterProcessing.ProcessedBySecurityIdentifier);
        Assert.Null(saved.PosterProcessing.Received);
        Assert.False(saved.PosterProcessing.Printed);
        Assert.False(saved.PosterProcessing.Laminated);
        Assert.False(saved.PosterProcessing.Notified);
        var folder = $"EVENTS/Example event, mentor and approval sheet required {saved.DateIn.Year} TO BE PRINTED/Ada Lovelace - {saved.PosterId}";
        var stem = $"{saved.PosterId} - Ada Lovelace - {saved.DateIn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        Assert.Equal($"{folder}/{stem}.pdf", saved.PosterFile.StoragePath);
        Assert.Equal($"{folder}/{stem} - Approval Sheet.pdf", saved.ApprovalSheet.StoragePath);
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
        SetName(missingMentor, "No Mentor");
        missingMentor.Room = "1";
        missingMentor.Phone = "555-0101";
        SetEmail(missingMentor, "mentor@example.edu");
        await using (var context = _database.CreateContext())
        {
            var outcome = await SubmitAsync(context, missingMentor);
            Assert.False(outcome.Saved);
            Assert.Equal("Enter the mentor for this event.", outcome.FieldErrors["mentor"]);
        }

        var optional = await CreateDraftAsync(department, openEvent.Id.ToString(), includeApproval: true);
        SetName(optional, "Open Event");
        optional.Room = "2";
        optional.Phone = "555-0102";
        SetEmail(optional, "open@example.edu");
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
        SetName(draft, "Landscape");
        draft.Room = "3";
        draft.Phone = "555-0103";
        SetEmail(draft, "landscape@example.edu");

        await using var context = _database.CreateContext();
        var outcome = await SubmitAsync(context, draft);

        Assert.True(outcome.Saved);
        var saved = await context.PosterRequests
            .Include(request => request.PosterFile)
            .SingleAsync(request => request.Name == "Landscape");
        Assert.Equal(36m, saved.PosterFile.Width);
        Assert.Equal(72m, saved.PosterFile.Length);
        var year = saved.DateIn.ToString("yyyy", CultureInfo.InvariantCulture);
        Assert.Equal($"WITHOUT-EVENT TO BE PRINTED/{year}/Landscape - {saved.PosterId}/{saved.PosterId} - Landscape - {saved.DateIn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf", saved.PosterFile.StoragePath);
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
        SetName(draft, "Content Wins");
        draft.Room = "4";
        draft.Phone = "555-0104";
        SetEmail(draft, "content@example.edu");

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
        Assert.EndsWith($"/{saved.PosterId} - Content Wins - {saved.DateIn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pptx", saved.PosterFile.StoragePath, StringComparison.Ordinal);
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
        SetName(draft, "Two Pages");
        draft.Mentor = "Kept";
        draft.Room = "5";
        draft.Phone = "555-0105";
        SetEmail(draft, "pages@example.edu");

        await using var context = _database.CreateContext();
        var outcome = await SubmitAsync(context, draft);

        Assert.False(outcome.Saved);
        Assert.Equal(
            "The PDF has 2 pages. A poster must contain exactly one page. Correct the file and upload it again.",
            outcome.FieldErrors["poster"]);
        Assert.True(_files.Exists(draft.DraftId, DraftFileRole.Poster));
        Assert.Equal(poster, await File.ReadAllBytesAsync(Path.Combine(_root, _files.PosterPath(draft.DraftId)!)));
        Assert.Equal(0, await context.PosterRequests.CountAsync(request => request.Name == "Two Pages"));
    }

    [Fact]
    public async Task A_non_pdf_approval_sheet_is_rejected_and_a_multi_page_sheet_is_accepted()
    {
        var choices = await LoadChoicesAsync();
        var approvalEvent = choices.Reasons.Single(reason => !reason.RequiresMentor && reason.RequiresApprovalSheet);
        var department = choices.Departments.First().Value;

        var invalid = await CreateDraftAsync(department, approvalEvent.Id.ToString(), includeApproval: false);
        SetName(invalid, "Bad Sheet");
        invalid.Room = "8";
        invalid.Phone = "555-0108";
        SetEmail(invalid, "sheet@example.edu");
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
        SetName(valid, "Multi Sheet");
        valid.Room = "9";
        valid.Phone = "555-0109";
        SetEmail(valid, "multi@example.edu");
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
        SetName(first, "First Sequence");
        first.Room = "10";
        first.Phone = "555-0110";
        SetEmail(first, "first@example.edu");
        var second = await CreateDraftAsync(department, "", includeApproval: false);
        SetName(second, "Second Sequence");
        second.Room = "11";
        second.Phone = "555-0111";
        SetEmail(second, "second@example.edu");
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

    // TDX Integration Contract section 17.15 (a), (b), (e).
    [Fact]
    public async Task Replacement_keeps_the_poster_id_and_ticket_and_archives_the_old_file_with_the_note()
    {
        var choices = await LoadChoicesAsync();
        var department = choices.Departments.First();
        var draft = await CreateDraftAsync(department.Value, "", includeApproval: false);
        FillRequester(draft, "Replace Keeper");
        await using var context = _database.CreateContext();
        var outcome = await SubmitAsync(context, draft);
        Assert.True(outcome.Saved);
        var saved = await context.PosterRequests.Include(request => request.PosterFile).SingleAsync(request => request.PosterId == outcome.PosterId);
        saved.TdxTicketId = 4_000_000 + Random.Shared.Next(1, 999_999);
        saved.TdxTicketState = PosterPrintRequest.Domain.TdxTicketStates.Created;
        await context.SaveChangesAsync();
        var oldBytes = await File.ReadAllBytesAsync(Full(saved.PosterFile.StoragePath));

        var replacementDraft = DraftIds.Create();
        var newPoster = SamplePosters.Pdf(24, 36);
        await SaveBytesAsync(replacementDraft, DraftFileRole.Poster, "Corrected.pdf", newPoster);
        await using (var replaceContext = _database.CreateContext())
        {
            var stranger = await Replacer(replaceContext).ReplaceAsync(
                outcome.PosterId, InstitutionalTestIdentities.Technician, replacementDraft, "Corrected.pdf", "Wrong author list", CancellationToken.None);
            Assert.False(stranger.Completed);
        }

        await using (var replaceContext = _database.CreateContext())
        {
            var result = await Replacer(replaceContext).ReplaceAsync(
                outcome.PosterId, InstitutionalTestIdentities.Requester, replacementDraft, "Corrected.pdf", "  Wrong author list  ", CancellationToken.None);
            Assert.True(result.Completed, result.Message);
        }

        await using var check = _database.CreateContext();
        var after = await check.PosterRequests
            .Include(request => request.PosterFile)
            .Include(request => request.Replacements)
            .SingleAsync(request => request.PosterId == outcome.PosterId);
        Assert.Equal(saved.PosterRequestId, after.PosterRequestId);
        Assert.Equal(saved.TdxTicketId, after.TdxTicketId);
        Assert.Equal(PosterPrintRequest.Domain.TdxTicketStates.Created, after.TdxTicketState);
        Assert.Equal(saved.PosterFile.StoragePath, after.PosterFile.StoragePath);
        Assert.Equal("Corrected.pdf", after.PosterFile.OriginalFileName);
        Assert.Equal(newPoster, await File.ReadAllBytesAsync(Full(after.PosterFile.StoragePath)));
        var replacement = Assert.Single(after.Replacements);
        Assert.Equal("Wrong author list", replacement.Note);
        Assert.Equal(InstitutionalTestIdentities.RequesterOid, replacement.ReplacedByObjectId);
        var folder = ReplacementNames.Folder(replacement.ReplacedAt.ToLocalTime(), outcome.PosterId!, saved.TdxTicketId);
        Assert.StartsWith(ReplacementNames.Root + "/" + folder + "/", replacement.ArchivedFilePath, StringComparison.Ordinal);
        Assert.EndsWith(" - " + saved.PosterFile.StoragePath[(saved.PosterFile.StoragePath.LastIndexOf('/') + 1)..], replacement.ArchivedFilePath, StringComparison.Ordinal);
        Assert.Equal(oldBytes, await File.ReadAllBytesAsync(Full(replacement.ArchivedFilePath)));
        var archivedName = replacement.ArchivedFilePath[(replacement.ArchivedFilePath.LastIndexOf('/') + 1)..];
        var noteFile = replacement.ArchivedFilePath[..^archivedName.Length] + archivedName[..9] + "Note.txt";
        Assert.Equal("Wrong author list", await File.ReadAllTextAsync(Full(noteFile)));
        Assert.False(_files.Exists(replacementDraft, DraftFileRole.Poster));
    }

    // Section 17.15 (c).
    [Fact]
    public async Task Replacement_of_a_printed_poster_returns_it_to_be_printed_and_clears_the_later_stages()
    {
        var choices = await LoadChoicesAsync();
        var department = choices.Departments.First();
        var draft = await CreateDraftAsync(department.Value, "", includeApproval: false);
        FillRequester(draft, "Printed Replacer");
        await using var context = _database.CreateContext();
        var outcome = await SubmitAsync(context, draft);
        Assert.True(outcome.Saved);
        var saved = await context.PosterRequests
            .Include(request => request.PosterFile)
            .Include(request => request.PosterProcessing)
            .SingleAsync(request => request.PosterId == outcome.PosterId);
        var toBePrinted = saved.PosterFile.StoragePath;
        Assert.True(PrintFolderPaths.TryMapPrintedFile(toBePrinted, out var printed));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetDirectoryName(Full(printed))!)!);
        Directory.Move(Path.GetDirectoryName(Full(toBePrinted))!, Path.GetDirectoryName(Full(printed))!);
        saved.PosterFile.StoragePath = printed;
        saved.PosterProcessing.Received = new DateOnly(2026, 11, 10);
        saved.PosterProcessing.Printed = true;
        saved.PosterProcessing.Notified = true;
        saved.PosterProcessing.DateOut = new DateOnly(2026, 11, 12);
        saved.PosterProcessing.PickedUpBy = "Front desk";
        await context.SaveChangesAsync();

        var replacementDraft = DraftIds.Create();
        await SaveBytesAsync(replacementDraft, DraftFileRole.Poster, "Fixed.pdf", SamplePosters.Pdf(30, 40));
        await using (var replaceContext = _database.CreateContext())
        {
            var result = await Replacer(replaceContext).ReplaceAsync(
                outcome.PosterId, InstitutionalTestIdentities.Requester, replacementDraft, "Fixed.pdf", "Typo in the title", CancellationToken.None);
            Assert.True(result.Completed, result.Message);
        }

        await using var check = _database.CreateContext();
        var after = await check.PosterRequests
            .Include(request => request.PosterFile)
            .Include(request => request.PosterProcessing)
            .SingleAsync(request => request.PosterId == outcome.PosterId);
        Assert.Equal(toBePrinted, after.PosterFile.StoragePath);
        Assert.True(File.Exists(Full(toBePrinted)));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Full(printed))));
        Assert.Equal(new DateOnly(2026, 11, 10), after.PosterProcessing.Received);
        Assert.False(after.PosterProcessing.Printed);
        Assert.False(after.PosterProcessing.Laminated);
        Assert.False(after.PosterProcessing.Notified);
        Assert.Null(after.PosterProcessing.DateOut);
        Assert.Null(after.PosterProcessing.PickedUpBy);
    }

    // Section 17.15 (d).
    [Fact]
    public async Task A_replacement_that_fails_preflight_or_has_no_note_changes_nothing()
    {
        var choices = await LoadChoicesAsync();
        var department = choices.Departments.First();
        var draft = await CreateDraftAsync(department.Value, "", includeApproval: false);
        FillRequester(draft, "Unchanged Requester");
        await using var context = _database.CreateContext();
        var outcome = await SubmitAsync(context, draft);
        Assert.True(outcome.Saved);
        var before = await context.PosterRequests.AsNoTracking().Include(request => request.PosterFile)
            .SingleAsync(request => request.PosterId == outcome.PosterId);
        var bytes = await File.ReadAllBytesAsync(Full(before.PosterFile.StoragePath));

        var wide = DraftIds.Create();
        await SaveBytesAsync(wide, DraftFileRole.Poster, "Wide.pdf", SamplePosters.Pdf(40, 72));
        await using (var replaceContext = _database.CreateContext())
        {
            var result = await Replacer(replaceContext).ReplaceAsync(
                outcome.PosterId, InstitutionalTestIdentities.Requester, wide, "Wide.pdf", "Too wide", CancellationToken.None);
            Assert.False(result.Completed);
            Assert.True(result.FieldErrors.ContainsKey("poster"));
        }

        var fine = DraftIds.Create();
        await SaveBytesAsync(fine, DraftFileRole.Poster, "Fine.pdf", SamplePosters.Pdf(24, 36));
        await using (var replaceContext = _database.CreateContext())
        {
            var result = await Replacer(replaceContext).ReplaceAsync(
                outcome.PosterId, InstitutionalTestIdentities.Requester, fine, "Fine.pdf", "   ", CancellationToken.None);
            Assert.False(result.Completed);
            Assert.True(result.FieldErrors.ContainsKey("note"));
        }

        await using var check = _database.CreateContext();
        var after = await check.PosterRequests.AsNoTracking().Include(request => request.PosterFile).Include(request => request.Replacements)
            .SingleAsync(request => request.PosterId == outcome.PosterId);
        Assert.Empty(after.Replacements);
        Assert.Equal(before.PosterFile.StoragePath, after.PosterFile.StoragePath);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Full(after.PosterFile.StoragePath)));
        Assert.False(Directory.Exists(Path.Combine(_root, ReplacementNames.Root))
            && Directory.EnumerateFiles(Path.Combine(_root, ReplacementNames.Root), "*", SearchOption.AllDirectories)
                .Any(path => path.Contains(outcome.PosterId!, StringComparison.Ordinal)));
    }

    // Authentication Contract section 3.9: without readable sign-in details the request is refused and nothing is stored.
    [Fact]
    public async Task A_request_without_sign_in_details_is_refused_and_nothing_is_stored()
    {
        var choices = await LoadChoicesAsync();
        var openEvent = choices.Reasons.Single(reason => !reason.RequiresMentor && !reason.RequiresApprovalSheet);
        var draft = await CreateDraftAsync(choices.Departments.First().Value, openEvent.Id.ToString(), includeApproval: false);
        draft.Room = "1";
        draft.Phone = "555-0100";
        await using var context = _database.CreateContext();
        var before = await context.PosterRequests.CountAsync();

        var outcome = await new RequesterSubmissionService(
            context,
            _files,
            new PosterPreflight(_files, TestStorage.Access(_root)),
            new AcceptedStorage(TestStorage.Access(_root), _files),
            new ApprovalSheetCheck(_files, TestStorage.Access(_root)),
            NullLogger<RequesterSubmissionService>.Instance,
            new SignedInAccount { Actor = InstitutionalTestIdentities.Requester }).SubmitAsync(draft, CancellationToken.None);

        Assert.False(outcome.Saved);
        Assert.Equal(RequesterContact.Unreadable, outcome.Message);
        Assert.Equal(before, await context.PosterRequests.CountAsync());
        Assert.True(_files.Exists(draft.DraftId, DraftFileRole.Poster));
    }

    [Fact]
    public async Task The_stored_name_and_email_are_the_sign_in_values()
    {
        var choices = await LoadChoicesAsync();
        var openEvent = choices.Reasons.Single(reason => !reason.RequiresMentor && !reason.RequiresApprovalSheet);
        var draft = await CreateDraftAsync(choices.Departments.First().Value, openEvent.Id.ToString(), includeApproval: false);
        draft.Room = "1";
        draft.Phone = "555-0100";
        SetName(draft, "Signed In Person");
        SetEmail(draft, "signed.in@uic.edu");
        await using var context = _database.CreateContext();

        var outcome = await SubmitAsync(context, draft);

        Assert.True(outcome.Saved);
        var saved = await context.PosterRequests.SingleAsync(request => request.PosterId == outcome.PosterId);
        Assert.Equal("Signed In Person", saved.Name);
        Assert.Equal("signed.in@uic.edu", saved.Email);
        Assert.Contains("Signed In Person - " + saved.PosterId, saved.PosterFile.StoragePath, StringComparison.Ordinal);
    }

    private PosterReplacementService Replacer(PosterPrintRequest.Infrastructure.Persistence.PosterPrintRequestDbContext context) =>
        new(
            context,
            TestStorage.Access(_root),
            _files,
            new PosterPreflight(_files, TestStorage.Access(_root)),
            new AcceptedStorage(TestStorage.Access(_root), _files),
            TimeProvider.System,
            NullLogger<PosterReplacementService>.Instance);

    private void FillRequester(RequesterDraft draft, string name)
    {
        SetName(draft, name);
        draft.Room = "1";
        draft.Phone = "555-0100";
        SetEmail(draft, "replace@example.edu");
    }

    private async Task<RequesterChoices> LoadChoicesAsync()
    {
        await using var context = _database.CreateContext();
        await RequesterOptionExamples.EnsureAsync(context);
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
            new PosterPreflight(_files, TestStorage.Access(_root)),
            new AcceptedStorage(TestStorage.Access(_root), _files),
            new ApprovalSheetCheck(_files, TestStorage.Access(_root)),
            logger,
            new SignedInAccount
            {
                Actor = InstitutionalTestIdentities.Requester,
                Contact = new RequesterContact(
                    _names.GetValueOrDefault(draft.DraftId, InstitutionalTestIdentities.RequesterContact.Name),
                    _emails.GetValueOrDefault(draft.DraftId, InstitutionalTestIdentities.RequesterContact.Email))
            }).SubmitAsync(draft, CancellationToken.None);

    private void SetName(RequesterDraft draft, string name) => _names[draft.DraftId] = name;

    private void SetEmail(RequesterDraft draft, string email) => _emails[draft.DraftId] = email;

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
            ReasonId = reasonId,
            PosterTitle = "Test poster"
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
