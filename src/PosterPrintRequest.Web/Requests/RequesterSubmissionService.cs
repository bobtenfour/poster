using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web.Requests;

public sealed class RequesterSubmissionService : IRequesterSubmission
{
    private readonly PosterPrintRequestDbContext _db;
    private readonly DraftFileStore _files;
    private readonly IPosterPreflight _preflight;
    private readonly AcceptedStorage _storage;
    private readonly IApprovalSheetCheck _approvalSheets;
    private readonly ILogger<RequesterSubmissionService> _logger;
    private readonly SignedInAccount? _account;

    public RequesterSubmissionService(
        PosterPrintRequestDbContext db,
        DraftFileStore files,
        IPosterPreflight preflight,
        AcceptedStorage storage,
        IApprovalSheetCheck approvalSheets,
        ILogger<RequesterSubmissionService> logger,
        SignedInAccount? account = null)
    {
        _db = db;
        _files = files;
        _preflight = preflight;
        _storage = storage;
        _approvalSheets = approvalSheets;
        _logger = logger;
        _account = account;
    }

    public async Task<SubmissionOutcome> SubmitAsync(RequesterDraft draft, CancellationToken cancellationToken)
    {
        var choices = await new RequesterOptionCatalog(_db).LoadAsync(cancellationToken);
        var errors = Validate(draft, choices);
        if (errors.Count > 0)
        {
            return SubmissionOutcome.Failure("Correct the highlighted fields.", errors);
        }

        if (!_files.Exists(draft.DraftId, DraftFileRole.Poster))
        {
            errors["poster"] = "Choose the poster file.";
            return SubmissionOutcome.Failure("Correct the highlighted fields.", errors);
        }

        var reason = choices.FindReason(draft.ReasonId);
        var needsApproval = reason?.RequiresApprovalSheet == true;
        if (needsApproval && !_files.Exists(draft.DraftId, DraftFileRole.ApprovalSheet))
        {
            errors["approval"] = ApprovalSheetRules.Missing;
            return SubmissionOutcome.Failure("Correct the highlighted fields.", errors);
        }

        var posterSource = _files.PosterPath(draft.DraftId);
        if (posterSource is null || draft.Poster is null)
        {
            return SubmissionOutcome.Failure("Choose the poster file again.");
        }

        var inspection = await _preflight.InspectStoredPosterAsync(draft.DraftId, cancellationToken);
        if (inspection is null || !inspection.Passed
            || inspection.DetectedFormat is null
            || inspection.PageCount is null
            || inspection.WidthInches is null
            || inspection.LengthInches is null)
        {
            errors["poster"] = inspection?.Summary ?? "Choose the poster file again.";
            return SubmissionOutcome.Failure("Correct the highlighted fields.", errors);
        }

        if (needsApproval)
        {
            var approvalError = await _approvalSheets.ValidateStoredAsync(draft.DraftId, cancellationToken);
            if (approvalError is not null)
            {
                errors["approval"] = approvalError;
                return SubmissionOutcome.Failure("Correct the highlighted fields.", errors);
            }
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var dateIn = DateTime.Now;
            var posterId = await NextPosterIdAsync(dateIn.Year, cancellationToken);
            if (posterId is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return SubmissionOutcome.Failure("The request could not be accepted. Submit it again.");
            }

            if (!_storage.TryPlace(
                    draft.DraftId,
                    posterId,
                    reason?.Name,
                    dateIn,
                    draft.Name.Trim(),
                    needsApproval,
                    inspection.DetectedFormat,
                    out var placement,
                    out var placeError)
                || placement is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return SubmissionOutcome.Failure(placeError ?? "The files could not be stored. Choose them again.");
            }

            var departmentId = int.Parse(draft.DepartmentId, CultureInfo.InvariantCulture);
            var departmentName = await _db.Departments
                .Where(department => department.DepartmentId == departmentId)
                .Select(department => department.Name)
                .SingleAsync(cancellationToken);
            string? reasonName = null;
            if (reason is not null)
            {
                reasonName = await _db.Reasons
                    .Where(item => item.ReasonId == reason.Id)
                    .Select(item => item.Name)
                    .SingleAsync(cancellationToken);
            }

            var request = new PosterRequest
            {
                PosterId = posterId,
                SubmittedByUserName = SignedInName(),
                Name = draft.Name.Trim(),
                Mentor = string.IsNullOrWhiteSpace(draft.Mentor) ? null : draft.Mentor.Trim(),
                DepartmentId = departmentId,
                DepartmentName = departmentName,
                Room = draft.Room.Trim(),
                Phone = draft.Phone.Trim(),
                Email = draft.Email.Trim(),
                ReasonId = reason?.Id,
                ReasonName = reasonName,
                LaminationRequested = draft.LaminationRequested,
                ApprovalSheetUploaded = needsApproval,
                DateIn = dateIn,
                PosterFile = new PosterFile
                {
                    OriginalFileName = SafeFileName(draft.Poster.OriginalFileName),
                    DetectedFormat = inspection.DetectedFormat,
                    PageCount = inspection.PageCount.Value,
                    Width = inspection.WidthInches.Value,
                    Length = inspection.LengthInches.Value,
                    StoragePath = placement.PosterRelative
                },
                PosterProcessing = new PosterProcessing()
            };

            if (needsApproval && draft.ApprovalSheet is not null && placement.ApprovalRelative is not null)
            {
                request.ApprovalSheet = new ApprovalSheet
                {
                    FileName = SafeFileName(draft.ApprovalSheet.OriginalFileName),
                    StoragePath = placement.ApprovalRelative
                };
            }

            _db.PosterRequests.Add(request);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception) && attempt < 3)
            {
                await transaction.RollbackAsync(cancellationToken);
                _storage.DeletePlacement(placement.DirectoryRelative);
                _db.ChangeTracker.Clear();
                continue;
            }
            catch (Exception)
            {
                await SafeRollbackAsync(transaction, cancellationToken);
                _storage.DeletePlacement(placement.DirectoryRelative);
                _db.ChangeTracker.Clear();
                return SubmissionOutcome.Failure("The request could not be accepted. Submit it again.");
            }

            try
            {
                _files.DeleteDraft(draft.DraftId);
            }
            catch (IOException)
            {
            }

            _logger.LogInformation(
                "Accepted poster {PosterId}. Event: {EventName}. Approval sheet uploaded: {ApprovalSheetUploaded}.",
                posterId,
                reason?.Name ?? "none",
                needsApproval);
            return SubmissionOutcome.Success(posterId);
        }

        return SubmissionOutcome.Failure("The request could not be accepted. Submit it again.");
    }

    public static Dictionary<string, string> Validate(RequesterDraft draft, RequesterChoices choices)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            errors["name"] = "Enter your name.";
        }

        if (!choices.Departments.Any(option => option.Value == draft.DepartmentId))
        {
            errors["department"] = "Choose a department.";
        }

        if (string.IsNullOrWhiteSpace(draft.Room))
        {
            errors["room"] = "Enter a room.";
        }

        if (string.IsNullOrWhiteSpace(draft.Phone))
        {
            errors["phone"] = "Enter a phone number.";
        }

        if (!IsEmail(draft.Email))
        {
            errors["email"] = "Enter an email address.";
        }

        var reason = choices.FindReason(draft.ReasonId);
        if (!string.IsNullOrWhiteSpace(draft.ReasonId) && reason is null)
        {
            errors["reason"] = "Choose an event from the list, or leave it blank.";
        }

        if (reason?.RequiresMentor == true && string.IsNullOrWhiteSpace(draft.Mentor))
        {
            errors["mentor"] = "Enter the mentor for this event.";
        }

        if (draft.Poster is null)
        {
            errors["poster"] = "Choose the poster file.";
        }

        if (reason?.RequiresApprovalSheet == true && draft.ApprovalSheet is null)
        {
            errors["approval"] = ApprovalSheetRules.Missing;
        }

        return errors;
    }

    private async Task<string?> NextPosterIdAsync(int year, CancellationToken cancellationToken)
    {
        var prefix = "POSTER-" + year.ToString("0000", CultureInfo.InvariantCulture) + "-";
        var existing = await _db.PosterRequests
            .Where(request => request.PosterId.StartsWith(prefix))
            .Select(request => request.PosterId)
            .ToListAsync(cancellationToken);
        var sequence = PosterIds.NextSequence(existing, year);
        return sequence > PosterIds.MaximumSequence ? null : PosterIds.Format(year, sequence);
    }

    private static bool IsEmail(string? value)
    {
        var email = value?.Trim() ?? "";
        var at = email.IndexOf('@');
        var dot = email.IndexOf('.', at + 1);
        return at > 0 && dot > at && dot < email.Length - 1;
    }

    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName).Trim();
        return name.Length <= 240 ? name : name[..240];
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number is 2601 or 2627)
            {
                return true;
            }
        }

        return false;
    }

    private string? SignedInName()
    {
        var name = _account?.UserName;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    private static async Task SafeRollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception)
        {
        }
    }
}
