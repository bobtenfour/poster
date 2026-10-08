using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web.Requests;

public sealed class TechnicianView
{
    public required string PosterId { get; init; }

    public required string RequesterName { get; init; }

    public required string Department { get; init; }

    public string? Mentor { get; init; }

    public string? EventName { get; init; }

    public required string Room { get; init; }

    public required string Phone { get; init; }

    public required string Email { get; init; }

    public required bool LaminationRequested { get; init; }

    public required DateTime DateIn { get; init; }

    public required string PosterFileName { get; init; }

    public string? ApprovalFileName { get; init; }

    public required string Folder { get; init; }

    public string? ItPerson { get; init; }

    public DateOnly? Received { get; init; }

    public bool Printed { get; init; }

    public bool Laminated { get; init; }

    public bool Notified { get; init; }

    public DateOnly? DateOut { get; init; }

    public string? PickedUpBy { get; init; }

    public string? Comments { get; init; }
}

public sealed class TechnicianOutcome
{
    public bool Completed { get; init; }

    public string? Message { get; init; }

    public static TechnicianOutcome Success() => new() { Completed = true };

    public static TechnicianOutcome Failure(string message) => new() { Completed = false, Message = message };
}

public interface ITechnicianWorkflow
{
    Task<TechnicianView?> FindAsync(string? posterId, CancellationToken cancellationToken);

    Task<TechnicianOutcome> TakeAsync(string posterId, string? itPerson, DateOnly? received, string? comments, CancellationToken cancellationToken);

    Task<TechnicianOutcome> MarkPrintedAsync(string posterId, string? comments, CancellationToken cancellationToken);

    Task<TechnicianOutcome> MarkLaminatedAsync(string posterId, string? comments, CancellationToken cancellationToken);

    Task<TechnicianOutcome> MarkNotifiedAsync(string posterId, string? comments, CancellationToken cancellationToken);

    Task<TechnicianOutcome> CompletePickupAsync(string posterId, DateOnly? dateOut, string? pickedUpBy, string? comments, CancellationToken cancellationToken);
}

public sealed class TechnicianWorkflow : ITechnicianWorkflow
{
    private readonly PosterPrintRequestDbContext _db;
    private readonly ILogger<TechnicianWorkflow> _logger;

    public TechnicianWorkflow(PosterPrintRequestDbContext db, ILogger<TechnicianWorkflow> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TechnicianView?> FindAsync(string? posterId, CancellationToken cancellationToken)
    {
        var request = await LoadAsync(posterId, cancellationToken);
        return request is null ? null : ToView(request);
    }

    public async Task<TechnicianOutcome> TakeAsync(
        string posterId,
        string? itPerson,
        DateOnly? received,
        string? comments,
        CancellationToken cancellationToken)
    {
        var request = await LoadAsync(posterId, cancellationToken);
        if (request is null)
        {
            return TechnicianOutcome.Failure("No request uses that Poster ID.");
        }

        if (request.PosterProcessing.Received is not null)
        {
            return TechnicianOutcome.Failure("This request has already been received.");
        }

        if (string.IsNullOrWhiteSpace(itPerson))
        {
            return TechnicianOutcome.Failure("Enter the IT person taking this request.");
        }

        if (received is null)
        {
            return TechnicianOutcome.Failure("Enter the date received.");
        }

        request.PosterProcessing.ITPerson = itPerson.Trim();
        request.PosterProcessing.Received = received;
        ApplyComments(request.PosterProcessing, comments);
        return await SaveStageAsync(request, "Received", cancellationToken);
    }

    public async Task<TechnicianOutcome> MarkPrintedAsync(string posterId, string? comments, CancellationToken cancellationToken)
    {
        var request = await LoadAsync(posterId, cancellationToken);
        if (request is null)
        {
            return TechnicianOutcome.Failure("No request uses that Poster ID.");
        }

        if (request.PosterProcessing.Received is null)
        {
            return TechnicianOutcome.Failure("Mark the poster received before continuing.");
        }

        if (request.PosterProcessing.Printed)
        {
            return TechnicianOutcome.Failure("This poster is already marked printed.");
        }

        request.PosterProcessing.Printed = true;
        ApplyComments(request.PosterProcessing, comments);
        return await SaveStageAsync(request, "Printed", cancellationToken);
    }

    public async Task<TechnicianOutcome> MarkLaminatedAsync(string posterId, string? comments, CancellationToken cancellationToken)
    {
        var request = await LoadAsync(posterId, cancellationToken);
        if (request is null)
        {
            return TechnicianOutcome.Failure("No request uses that Poster ID.");
        }

        if (!request.LaminationRequested)
        {
            return TechnicianOutcome.Failure("Lamination was not requested.");
        }

        if (!request.PosterProcessing.Printed)
        {
            return TechnicianOutcome.Failure("Mark the poster printed before continuing.");
        }

        if (request.PosterProcessing.Laminated)
        {
            return TechnicianOutcome.Failure("This poster is already marked laminated.");
        }

        request.PosterProcessing.Laminated = true;
        ApplyComments(request.PosterProcessing, comments);
        return await SaveStageAsync(request, "Laminated", cancellationToken);
    }

    public async Task<TechnicianOutcome> MarkNotifiedAsync(string posterId, string? comments, CancellationToken cancellationToken)
    {
        var request = await LoadAsync(posterId, cancellationToken);
        if (request is null)
        {
            return TechnicianOutcome.Failure("No request uses that Poster ID.");
        }

        if (!request.PosterProcessing.Printed)
        {
            return TechnicianOutcome.Failure("Mark the poster printed before continuing.");
        }

        if (request.LaminationRequested && !request.PosterProcessing.Laminated)
        {
            return TechnicianOutcome.Failure("Mark lamination before continuing.");
        }

        if (request.PosterProcessing.Notified)
        {
            return TechnicianOutcome.Failure("The requester is already marked notified.");
        }

        request.PosterProcessing.Notified = true;
        ApplyComments(request.PosterProcessing, comments);
        return await SaveStageAsync(request, "Notified", cancellationToken);
    }

    public async Task<TechnicianOutcome> CompletePickupAsync(
        string posterId,
        DateOnly? dateOut,
        string? pickedUpBy,
        string? comments,
        CancellationToken cancellationToken)
    {
        var request = await LoadAsync(posterId, cancellationToken);
        if (request is null)
        {
            return TechnicianOutcome.Failure("No request uses that Poster ID.");
        }

        if (!request.PosterProcessing.Notified)
        {
            return TechnicianOutcome.Failure("Mark the requester notified before pickup.");
        }

        if (request.PosterProcessing.DateOut is not null)
        {
            return TechnicianOutcome.Failure("Pickup is already complete.");
        }

        if (dateOut is null)
        {
            return TechnicianOutcome.Failure("Enter the date out.");
        }

        if (string.IsNullOrWhiteSpace(pickedUpBy))
        {
            return TechnicianOutcome.Failure("Enter who picked up the poster.");
        }

        request.PosterProcessing.DateOut = dateOut;
        request.PosterProcessing.PickedUpBy = pickedUpBy.Trim();
        ApplyComments(request.PosterProcessing, comments);
        return await SaveStageAsync(request, "Pickup", cancellationToken);
    }

    private async Task<PosterRequest?> LoadAsync(string? posterId, CancellationToken cancellationToken)
    {
        if (!PosterIds.IsPublic(posterId))
        {
            return null;
        }

        return await _db.PosterRequests
            .Include(request => request.PosterProcessing)
            .Include(request => request.PosterFile)
            .Include(request => request.ApprovalSheet)
            .SingleOrDefaultAsync(request => request.PosterId == posterId, cancellationToken);
    }

    private async Task<TechnicianOutcome> SaveStageAsync(PosterRequest request, string stage, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return TechnicianOutcome.Failure("The request could not be updated.");
        }

        _logger.LogInformation(
            "Technician recorded {Stage} for poster {PosterId}.",
            stage,
            request.PosterId);
        return TechnicianOutcome.Success();
    }

    private static void ApplyComments(PosterProcessing processing, string? comments)
    {
        processing.Comments = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim();
    }

    private static TechnicianView ToView(PosterRequest request)
    {
        var processing = request.PosterProcessing;
        return new TechnicianView
        {
            PosterId = request.PosterId,
            RequesterName = request.Name,
            Department = request.DepartmentName,
            Mentor = request.Mentor,
            EventName = request.ReasonName,
            Room = request.Room,
            Phone = request.Phone,
            Email = request.Email,
            LaminationRequested = request.LaminationRequested,
            DateIn = request.DateIn,
            PosterFileName = FileName(request.PosterFile.StoragePath),
            ApprovalFileName = request.ApprovalSheet is null ? null : FileName(request.ApprovalSheet.StoragePath),
            Folder = DirectoryOf(request.PosterFile.StoragePath),
            ItPerson = processing.ITPerson,
            Received = processing.Received,
            Printed = processing.Printed,
            Laminated = processing.Laminated,
            Notified = processing.Notified,
            DateOut = processing.DateOut,
            PickedUpBy = processing.PickedUpBy,
            Comments = processing.Comments
        };
    }

    private static string DirectoryOf(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash <= 0 ? normalized : normalized[..slash];
    }

    private static string FileName(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }
}
