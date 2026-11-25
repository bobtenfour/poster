using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Infrastructure.Storage;

namespace PosterPrintRequest.Web.Requests;

/// <summary>What the replacement page shows about the request (TDX Integration Contract section 17).</summary>
public sealed record ReplacementTarget(
    string PosterId,
    string? PosterTitle,
    string CurrentFileName,
    bool Printed,
    int? TdxTicketId,
    int Replacements);

public sealed class ReplacementOutcome
{
    public bool Completed { get; init; }

    public string? Message { get; init; }

    public IReadOnlyDictionary<string, string> FieldErrors { get; init; } = new Dictionary<string, string>();

    public static ReplacementOutcome Success() => new() { Completed = true };

    public static ReplacementOutcome Failure(string message, Dictionary<string, string>? errors = null) =>
        new() { Completed = false, Message = message, FieldErrors = errors ?? new Dictionary<string, string>() };
}

public interface IPosterReplacement
{
    Task<ReplacementTarget?> FindAsync(string? posterId, InstitutionalActor? actor, CancellationToken cancellationToken);

    Task<ReplacementOutcome> ReplaceAsync(
        string? posterId,
        InstitutionalActor? actor,
        string draftId,
        string originalFileName,
        string? note,
        CancellationToken cancellationToken);
}

/// <summary>Archive folder and file names of section 17.6.</summary>
public static class ReplacementNames
{
    public const string Root = "sustitutions";

    /// <summary>
    /// The name shown on screen for <see cref="Root"/>, in capitals like the other library cards
    /// (section 17.11 clarification). The folder on the share keeps the name <see cref="Root"/> (section 17.6).
    /// </summary>
    public const string Label = "SUSTITUTIONS";

    public static string Display(string segment) => segment == Root ? Label : segment;

    public const int NoteMaxLength = 500;

    public static string FolderPrefix(DateTime localNow, string posterId) =>
        localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + posterId + " TDX-";

    public static string Folder(DateTime localNow, string posterId, int? ticketId) =>
        FolderPrefix(localNow, posterId)
        + (ticketId is int id ? id.ToString(CultureInfo.InvariantCulture) : "PENDING");

    public static string ArchivedPoster(DateTime localNow, string currentPosterFileName) =>
        localNow.ToString("HHmmss", CultureInfo.InvariantCulture) + " - " + currentPosterFileName;

    public static string Note(DateTime localNow) =>
        localNow.ToString("HHmmss", CultureInfo.InvariantCulture) + " - Note.txt";

    public const string Tone = "sustitutions";

    /// <summary>A folder of section 17.6: "yyyy-MM-dd POSTER-yyyy-nnnnnn TDX-n" or "... TDX-PENDING".</summary>
    public static bool IsArchiveFolder(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var parts = name.Split(' ');
        if (parts.Length != 3
            || !DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || !PosterIds.IsPublic(parts[1])
            || !parts[2].StartsWith("TDX-", StringComparison.Ordinal))
        {
            return false;
        }

        var ticket = parts[2][4..];
        return ticket == "PENDING"
            || (ticket.Length is > 0 and <= 10 && ticket.All(char.IsAsciiDigit));
    }

    /// <summary>A file of section 17.6: "HHmmss - Poster.pdf", "HHmmss - Poster.pptx" or "HHmmss - Note.txt".</summary>
    public static bool IsArchiveFile(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length < 10 || name.Substring(6, 3) != " - ")
        {
            return false;
        }

        return DateTime.TryParseExact(name[..6], "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            && name[9..] is StorageNames.PosterPdf or StorageNames.PosterPptx or "Note.txt";
    }

    /// <summary>Section 17.5: trimmed, 1 to 500 characters, no control characters.</summary>
    public static string? CheckNote(string? value)
    {
        var note = value?.Trim() ?? "";
        if (note.Length == 0)
        {
            return "Write a short note that explains the error.";
        }

        if (note.Length > NoteMaxLength)
        {
            return $"Use {NoteMaxLength} characters or fewer.";
        }

        return note.Any(char.IsControl) ? "Write the note on one line, without line breaks or tabs." : null;
    }
}

/// <summary>
/// Replaces the poster file of an accepted request by its own requester (TDX Integration Contract section 17).
/// The Poster ID, the request and the TDX ticket stay; no ticket is created or updated. Every file operation goes
/// through the storage boundary (section 17.13).
/// </summary>
public sealed class PosterReplacementService : IPosterReplacement
{
    private const string GenericFailure = "The poster could not be replaced. Nothing was changed. Try again.";

    private readonly PosterPrintRequestDbContext _db;
    private readonly ISharedStorageAccess _access;
    private readonly DraftFileStore _drafts;
    private readonly IPosterPreflight _preflight;
    private readonly AcceptedStorage _storage;
    private readonly TimeProvider _time;
    private readonly ILogger<PosterReplacementService> _logger;

    public PosterReplacementService(
        PosterPrintRequestDbContext db,
        ISharedStorageAccess access,
        DraftFileStore drafts,
        IPosterPreflight preflight,
        AcceptedStorage storage,
        TimeProvider time,
        ILogger<PosterReplacementService> logger)
    {
        _db = db;
        _access = access;
        _drafts = drafts;
        _preflight = preflight;
        _storage = storage;
        _time = time;
        _logger = logger;
    }

    public async Task<ReplacementTarget?> FindAsync(string? posterId, InstitutionalActor? actor, CancellationToken cancellationToken)
    {
        if (!PosterIds.IsPublic(posterId) || actor is null || !actor.IsValid)
        {
            return null;
        }

        await HistoricalLinking.LinkAsync(_db, actor, cancellationToken);
        return await _db.PosterRequests
            .AsNoTracking()
            .Where(request => request.PosterId == posterId
                && request.SubmittedByObjectId == actor.ObjectId
                && request.TemporaryDeletionAt == null
                && request.PermanentlyDeletedAt == null)
            .Select(request => new ReplacementTarget(
                request.PosterId,
                request.PosterTitle,
                request.PosterFile.OriginalFileName,
                request.PosterProcessing.Printed,
                request.TdxTicketId,
                request.Replacements.Count))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ReplacementOutcome> ReplaceAsync(
        string? posterId,
        InstitutionalActor? actor,
        string draftId,
        string originalFileName,
        string? note,
        CancellationToken cancellationToken)
    {
        // Section 17.2: only the requester of the request.
        if (!PosterIds.IsPublic(posterId) || actor is null || !actor.IsValid)
        {
            return ReplacementOutcome.Failure("The signed-in identity could not be verified.");
        }

        var errors = new Dictionary<string, string>();
        var noteError = ReplacementNames.CheckNote(note);
        if (noteError is not null)
        {
            errors["note"] = noteError;
        }

        if (!DraftIds.IsValid(draftId) || !_drafts.Exists(draftId, DraftFileRole.Poster))
        {
            errors["poster"] = "Choose the new poster file.";
        }

        if (errors.Count > 0)
        {
            return ReplacementOutcome.Failure("Correct the highlighted fields.", errors);
        }

        // Section 17.4: the same preflight as a new request. If it fails, nothing changes.
        var inspection = await _preflight.InspectStoredPosterAsync(draftId, cancellationToken);
        if (inspection is null || !inspection.Passed
            || inspection.DetectedFormat is null
            || inspection.PageCount is null
            || inspection.WidthInches is null
            || inspection.LengthInches is null)
        {
            errors["poster"] = inspection?.Summary ?? "Choose the new poster file again.";
            return ReplacementOutcome.Failure("Correct the highlighted fields.", errors);
        }

        var draftPoster = _drafts.PosterPath(draftId);
        if (draftPoster is null)
        {
            errors["poster"] = "Choose the new poster file again.";
            return ReplacementOutcome.Failure("Correct the highlighted fields.", errors);
        }

        await HistoricalLinking.LinkAsync(_db, actor, cancellationToken);
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Section 17.9: one replacement at a time per request. The row lock is held until the transaction ends.
        var locked = await _db.PosterRequests
            .FromSqlInterpolated($"SELECT * FROM [PosterRequests] WITH (UPDLOCK, HOLDLOCK) WHERE [PosterId] = {posterId}")
            .Select(request => request.PosterRequestId)
            .ToListAsync(cancellationToken);
        if (locked.Count != 1)
        {
            return ReplacementOutcome.Failure("No request of yours uses that Poster ID.");
        }

        var request = await _db.PosterRequests
            .Include(item => item.PosterFile)
            .Include(item => item.ApprovalSheet)
            .Include(item => item.PosterProcessing)
            .SingleAsync(item => item.PosterRequestId == locked[0], cancellationToken);
        if (request.SubmittedByObjectId != actor.ObjectId)
        {
            return ReplacementOutcome.Failure("No request of yours uses that Poster ID.");
        }

        if (request.TemporaryDeletionAt is not null || request.PermanentlyDeletedAt is not null)
        {
            return ReplacementOutcome.Failure("This poster was deleted and cannot be replaced.");
        }

        var nowUtc = _time.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, _time.LocalTimeZone).DateTime;
        var trimmedNote = note!.Trim();
        var plan = Plan(request, inspection.DetectedFormat, localNow);
        if (plan is null)
        {
            return ReplacementOutcome.Failure(GenericFailure);
        }

        var undo = new Stack<Action>();
        try
        {
            ExecuteFiles(plan, draftPoster, trimmedNote, undo);
        }
        catch (Exception)
        {
            Undo(undo, request.PosterId);
            return ReplacementOutcome.Failure(GenericFailure);
        }

        var previous = Snapshot(request);
        request.PosterFile.StoragePath = plan.NewPoster;
        request.PosterFile.OriginalFileName = SafeFileName(originalFileName);
        request.PosterFile.DetectedFormat = inspection.DetectedFormat;
        request.PosterFile.PageCount = inspection.PageCount.Value;
        request.PosterFile.Width = inspection.WidthInches.Value;
        request.PosterFile.Length = inspection.LengthInches.Value;
        if (request.ApprovalSheet is not null && plan.NewApproval is not null)
        {
            request.ApprovalSheet.StoragePath = plan.NewApproval;
        }

        if (plan.MoveBack)
        {
            // Section 17.8: back to be printed; Received is kept.
            request.PosterProcessing.Printed = false;
            request.PosterProcessing.Laminated = false;
            request.PosterProcessing.Notified = false;
            request.PosterProcessing.DateOut = null;
            request.PosterProcessing.PickedUpBy = null;
        }

        _db.PosterReplacements.Add(new PosterReplacement
        {
            PosterRequestId = request.PosterRequestId,
            ReplacedAt = nowUtc.UtcDateTime,
            Note = trimmedNote,
            ArchivedFilePath = plan.ArchivedPoster,
            ReplacedByObjectId = actor.ObjectId
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception)
        {
            await SafeRollbackAsync(transaction);
            previous.Restore(request);
            _db.ChangeTracker.Clear();
            Undo(undo, request.PosterId);
            return ReplacementOutcome.Failure(GenericFailure);
        }

        try
        {
            _drafts.DeleteDraft(draftId);
        }
        catch (IOException)
        {
        }

        _logger.LogInformation(
            "Poster {PosterId} was replaced by its requester. Moved back to be printed: {MovedBack}.",
            request.PosterId,
            plan.MoveBack);
        return ReplacementOutcome.Success();
    }

    private sealed record ReplacementPlan(
        string CurrentPoster,
        string CurrentDirectory,
        string ArchiveFolder,
        string ArchivedPoster,
        string Note,
        string PlacedPoster,
        string NewPoster,
        bool MoveBack,
        string? TargetDirectory,
        string? NewApproval);

    private ReplacementPlan? Plan(PosterRequest request, string detectedFormat, DateTime localNow)
    {
        var current = request.PosterFile.StoragePath.Replace('\\', '/');
        var directory = PrintFolderPaths.DirectoryOf(current);
        if (directory is null || !_access.FileExists(current))
        {
            return null;
        }

        var currentName = current[(current.LastIndexOf('/') + 1)..];
        if (currentName is not (StorageNames.PosterPdf or StorageNames.PosterPptx))
        {
            return null;
        }

        var folder = ExistingFolder(localNow, request.PosterId) ?? ReplacementNames.Folder(localNow, request.PosterId, request.TdxTicketId);
        var archiveFolder = ReplacementNames.Root + "/" + folder;
        var placed = directory + "/" + StorageNames.PosterFileName(detectedFormat);
        var moveBack = request.PosterProcessing.Printed;
        string? target = null;
        string? newApproval = null;
        var newPoster = placed;
        if (moveBack)
        {
            if (!PrintFolderPaths.TryMapToBePrintedFile(placed, out var mapped))
            {
                return null;
            }

            target = PrintFolderPaths.DirectoryOf(mapped);
            newPoster = mapped;
            if (target is null)
            {
                return null;
            }

            if (request.ApprovalSheet is not null)
            {
                newApproval = PrintFolderPaths.RewritePrefix(request.ApprovalSheet.StoragePath, directory, target);
                if (newApproval is null)
                {
                    return null;
                }
            }
        }

        return new ReplacementPlan(
            current,
            directory,
            archiveFolder,
            archiveFolder + "/" + ReplacementNames.ArchivedPoster(localNow, currentName),
            archiveFolder + "/" + ReplacementNames.Note(localNow),
            placed,
            newPoster,
            moveBack,
            target,
            newApproval);
    }

    /// <summary>Section 17.6: several replacements on the same day share the folder; its name does not change later.</summary>
    private string? ExistingFolder(DateTime localNow, string posterId)
    {
        if (!_access.DirectoryExists(ReplacementNames.Root))
        {
            return null;
        }

        var prefix = ReplacementNames.FolderPrefix(localNow, posterId);
        return _access.ListEntries(ReplacementNames.Root)
            .Where(entry => entry.IsDirectory && entry.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(entry => entry.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>Section 17.9 order: archive the old file and write the note, place the new file, move the directory.</summary>
    private void ExecuteFiles(ReplacementPlan plan, string draftPoster, string note, Stack<Action> undo)
    {
        if (_access.FileExists(plan.ArchivedPoster) || _access.FileExists(plan.Note))
        {
            throw new IOException("An archived file already exists. History is never overwritten.");
        }

        if (!_access.DirectoryExists(ReplacementNames.Root))
        {
            _access.CreateDirectory(ReplacementNames.Root);
        }

        if (!_access.DirectoryExists(plan.ArchiveFolder))
        {
            _access.CreateDirectory(plan.ArchiveFolder);
            undo.Push(() => DeleteDirectoryIfEmpty(plan.ArchiveFolder));
        }

        _access.CopyFile(plan.CurrentPoster, plan.ArchivedPoster);
        undo.Push(() => _access.DeleteFile(plan.ArchivedPoster));

        using (var stream = _access.OpenWrite(plan.Note))
        {
            undo.Push(() => _access.DeleteFile(plan.Note));
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(note);
            stream.Write(bytes, 0, bytes.Length);
        }

        _access.DeleteFile(plan.CurrentPoster);
        undo.Push(() =>
        {
            if (!_access.FileExists(plan.CurrentPoster))
            {
                _access.CopyFile(plan.ArchivedPoster, plan.CurrentPoster);
            }
        });

        _access.CopyFile(draftPoster, plan.PlacedPoster);
        undo.Push(() => _access.DeleteFile(plan.PlacedPoster));
        if (!_access.FileExists(plan.PlacedPoster))
        {
            throw new IOException("The new poster file was not placed.");
        }

        if (plan.MoveBack)
        {
            if (!_storage.TryMoveDirectory(plan.CurrentDirectory, plan.TargetDirectory))
            {
                throw new IOException("The poster directory could not be moved back to be printed.");
            }

            undo.Push(() =>
            {
                if (!_storage.TryMoveDirectory(plan.TargetDirectory, plan.CurrentDirectory))
                {
                    throw new IOException("The poster directory could not be moved back.");
                }
            });
            if (!_access.FileExists(plan.NewPoster))
            {
                throw new IOException("The moved poster file was not found.");
            }
        }
    }

    private void DeleteDirectoryIfEmpty(string directory)
    {
        if (_access.DirectoryExists(directory) && _access.ListEntries(directory).Count == 0)
        {
            _access.DeleteDirectory(directory);
        }
    }

    private void Undo(Stack<Action> undo, string posterId)
    {
        while (undo.Count > 0)
        {
            var step = undo.Pop();
            try
            {
                step();
            }
            catch (Exception)
            {
                _logger.LogError("A file step of the replacement of poster {PosterId} could not be undone.", posterId);
            }
        }
    }

    private static async Task SafeRollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }

    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName ?? "").Trim();
        if (name.Length == 0)
        {
            name = "Poster";
        }

        return name.Length <= 240 ? name : name[..240];
    }

    private static RequestSnapshot Snapshot(PosterRequest request) => new(
        request.PosterFile.StoragePath,
        request.PosterFile.OriginalFileName,
        request.PosterFile.DetectedFormat,
        request.PosterFile.PageCount,
        request.PosterFile.Width,
        request.PosterFile.Length,
        request.ApprovalSheet?.StoragePath,
        request.PosterProcessing.Printed,
        request.PosterProcessing.Laminated,
        request.PosterProcessing.Notified,
        request.PosterProcessing.DateOut,
        request.PosterProcessing.PickedUpBy);

    private sealed record RequestSnapshot(
        string StoragePath,
        string OriginalFileName,
        string DetectedFormat,
        int PageCount,
        decimal Width,
        decimal Length,
        string? ApprovalPath,
        bool Printed,
        bool Laminated,
        bool Notified,
        DateOnly? DateOut,
        string? PickedUpBy)
    {
        public void Restore(PosterRequest request)
        {
            request.PosterFile.StoragePath = StoragePath;
            request.PosterFile.OriginalFileName = OriginalFileName;
            request.PosterFile.DetectedFormat = DetectedFormat;
            request.PosterFile.PageCount = PageCount;
            request.PosterFile.Width = Width;
            request.PosterFile.Length = Length;
            if (request.ApprovalSheet is not null && ApprovalPath is not null)
            {
                request.ApprovalSheet.StoragePath = ApprovalPath;
            }

            request.PosterProcessing.Printed = Printed;
            request.PosterProcessing.Laminated = Laminated;
            request.PosterProcessing.Notified = Notified;
            request.PosterProcessing.DateOut = DateOut;
            request.PosterProcessing.PickedUpBy = PickedUpBy;
        }
    }
}
