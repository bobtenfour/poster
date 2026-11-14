using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Infrastructure.Storage;

namespace PosterPrintRequest.Web.Requests;

public static class PosterRetentionPaths
{
    public const string FolderName = "borrado_temporal";

    public static readonly TimeSpan Retention = TimeSpan.FromHours(48);

    public static string RetentionDirectory(string originalDirectory) =>
        FolderName + "/" + originalDirectory.Trim('/');
}

public static class PosterRetentionAction
{
    public const string TemporaryDeletion = "TemporaryDeletion";

    public const string Restoration = "Restoration";

    public const string PermanentExpiration = "PermanentExpiration";
}

public static class PosterRetentionMessages
{
    public const string UnauthorizedDelete = "You are not authorized to delete this poster.";

    public const string UnauthorizedRestore = "You are not authorized to restore this poster.";

    public const string Missing = "No request uses that Poster ID.";

    public const string AlreadyDeleted = "This poster is already deleted.";

    public const string NotDeleted = "This poster is not deleted.";

    public const string Ended = "The retention period has ended.";

    public const string Unavailable = "This poster is no longer available.";

    public const string DeleteFailed = "The poster could not be deleted.";

    public const string DeleteConflict = "The poster could not be deleted because the retention location is not empty.";

    public const string RestoreFailed = "The poster could not be restored.";

    public const string RestoreConflict = "The poster could not be restored because its original location is not empty.";

    public const string ListFailed = "Temporarily deleted posters could not be listed.";
}

public sealed record RetainedPosterListing(string PosterId, string FolderName, string ParentDirectory);

/// <summary>
/// Wakes expiration processing when a poster enters or leaves retention.
/// Permanent expiration does not signal, because the processor is already running that work.
/// </summary>
public sealed class PosterRetentionSchedule
{
    private readonly System.Threading.Channels.Channel<byte> _channel =
        System.Threading.Channels.Channel.CreateUnbounded<byte>();

    public void Wake() => _channel.Writer.TryWrite(0);

    public System.Threading.Channels.ChannelReader<byte> Reader => _channel.Reader;
}

public interface IPosterRetention
{
    Task<RetentionOutcome> ConfirmDeletionAsync(string? posterId, ClaimsPrincipal? user, CancellationToken cancellationToken);

    Task<RetentionOutcome> RestoreAsync(string? posterId, ClaimsPrincipal? user, CancellationToken cancellationToken);

    Task<IReadOnlyList<RetainedPosterListing>> ListRetainedAsync(CancellationToken cancellationToken);

    Task ExpireAsync(CancellationToken cancellationToken);

    Task<TimeSpan> DelayUntilNextExpirationAsync(CancellationToken cancellationToken);
}

public sealed class RetentionOutcome
{
    public bool Completed { get; init; }

    public string? Message { get; init; }

    public static RetentionOutcome Success() => new() { Completed = true };

    public static RetentionOutcome Failure(string message) => new() { Completed = false, Message = message };
}

public sealed class PosterRetention : IPosterRetention
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly PosterPrintRequestDbContext _db;
    private readonly ISharedStorageAccess _access;
    private readonly IAuthorizationService _authorization;
    private readonly IInstitutionalActorSource _actors;
    private readonly TimeProvider _time;
    private readonly PosterRetentionSchedule _schedule;
    private readonly ILogger<PosterRetention> _logger;

    public PosterRetention(
        PosterPrintRequestDbContext db,
        ISharedStorageAccess access,
        IAuthorizationService authorization,
        IInstitutionalActorSource actors,
        TimeProvider time,
        PosterRetentionSchedule schedule,
        ILogger<PosterRetention> logger)
    {
        _db = db;
        _access = access;
        _authorization = authorization;
        _actors = actors;
        _time = time;
        _schedule = schedule;
        _logger = logger;
    }

    public async Task<RetentionOutcome> ConfirmDeletionAsync(
        string? posterId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken)
    {
        if (!PosterIds.IsPublic(posterId))
        {
            return RetentionOutcome.Failure(PosterRetentionMessages.Missing);
        }

        if (!await IsTechnicianAsync(user))
        {
            _logger.LogWarning(
                "Operation {Operation} for poster {PosterId} was denied.",
                PosterRetentionAction.TemporaryDeletion,
                posterId);
            return RetentionOutcome.Failure(PosterRetentionMessages.UnauthorizedDelete);
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await DeleteLockedAsync(posterId!, user, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<RetentionOutcome> RestoreAsync(
        string? posterId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken)
    {
        if (!PosterIds.IsPublic(posterId))
        {
            return RetentionOutcome.Failure(PosterRetentionMessages.Missing);
        }

        if (!await IsTechnicianAsync(user))
        {
            _logger.LogWarning(
                "Operation {Operation} for poster {PosterId} was denied.",
                PosterRetentionAction.Restoration,
                posterId);
            return RetentionOutcome.Failure(PosterRetentionMessages.UnauthorizedRestore);
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await RestoreLockedAsync(posterId!, user, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<RetainedPosterListing>> ListRetainedAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var rows = await _db.PosterRequests
            .AsNoTracking()
            .Where(request => request.TemporaryDeletionAt != null
                && request.PermanentlyDeletedAt == null
                && request.OriginalDirectory != null)
            .Select(request => new { request.PosterId, request.OriginalDirectory, request.TemporaryDeletionAt })
            .ToListAsync(cancellationToken);

        var listings = new List<RetainedPosterListing>();
        foreach (var row in rows)
        {
            if (row.TemporaryDeletionAt is not { } deletedAt || AsUtc(deletedAt).Add(PosterRetentionPaths.Retention) <= now)
            {
                continue;
            }

            if (!TrySplitDirectory(row.OriginalDirectory, out var parent, out var folder))
            {
                continue;
            }

            listings.Add(new RetainedPosterListing(row.PosterId, folder, parent));
        }

        return listings;
    }

    public async Task ExpireAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var cutoff = UtcNow().Subtract(PosterRetentionPaths.Retention);
            var due = await _db.PosterRequests
                .Where(request => request.TemporaryDeletionAt != null
                    && request.PermanentlyDeletedAt == null
                    && request.TemporaryDeletionAt <= cutoff)
                .ToListAsync(cancellationToken);
            foreach (var request in due)
            {
                await ExpireLockedAsync(request, cancellationToken);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<TimeSpan> DelayUntilNextExpirationAsync(CancellationToken cancellationToken)
    {
        var soonest = await _db.PosterRequests
            .AsNoTracking()
            .Where(request => request.TemporaryDeletionAt != null && request.PermanentlyDeletedAt == null)
            .MinAsync(request => (DateTime?)request.TemporaryDeletionAt, cancellationToken);
        if (soonest is null)
        {
            return TimeSpan.FromHours(1);
        }

        var delay = AsUtc(soonest.Value).Add(PosterRetentionPaths.Retention) - UtcNow();
        if (delay < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return delay > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : delay;
    }

    private async Task<RetentionOutcome> DeleteLockedAsync(
        string posterId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken)
    {
        var request = await LoadAsync(posterId, cancellationToken);
        if (request is null)
        {
            return RetentionOutcome.Failure(PosterRetentionMessages.Missing);
        }

        if (request.PermanentlyDeletedAt is not null)
        {
            return RetentionOutcome.Failure(PosterRetentionMessages.Unavailable);
        }

        if (request.TemporaryDeletionAt is not null)
        {
            return RetentionOutcome.Failure(PosterRetentionMessages.AlreadyDeleted);
        }

        if (!TryReadActiveDirectory(request, out var source))
        {
            return RetentionOutcome.Failure(PosterRetentionMessages.DeleteFailed);
        }

        var destination = PosterRetentionPaths.RetentionDirectory(source);
        if (_access.DirectoryExists(destination) || _access.FileExists(destination))
        {
            return RetentionOutcome.Failure(PosterRetentionMessages.DeleteConflict);
        }

        if (!TryMove(source, destination))
        {
            return RetentionOutcome.Failure(PosterRetentionMessages.DeleteFailed);
        }

        var previousPoster = request.PosterFile.StoragePath;
        var previousApproval = request.ApprovalSheet?.StoragePath;
        var retentionPoster = PrintFolderPaths.RewritePrefix(previousPoster, source, destination);
        var retentionApproval = previousApproval is null
            ? null
            : PrintFolderPaths.RewritePrefix(previousApproval, source, destination);
        if (retentionPoster is null || (previousApproval is not null && retentionApproval is null))
        {
            TryMove(destination, source);
            return RetentionOutcome.Failure(PosterRetentionMessages.DeleteFailed);
        }

        var now = UtcNow();
        request.TemporaryDeletionAt = now;
        request.OriginalDirectory = source;
        request.PosterFile.StoragePath = retentionPoster;
        if (request.ApprovalSheet is not null)
        {
            request.ApprovalSheet.StoragePath = retentionApproval!;
        }

        var actor = ActorId(user);
        _db.PosterRetentionAudits.Add(Audit(request, PosterRetentionAction.TemporaryDeletion, now, actor, source));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            TryMove(destination, source);
            request.TemporaryDeletionAt = null;
            request.OriginalDirectory = null;
            request.PosterFile.StoragePath = previousPoster;
            if (request.ApprovalSheet is not null && previousApproval is not null)
            {
                request.ApprovalSheet.StoragePath = previousApproval;
            }

            DetachAddedAudits();
            return RetentionOutcome.Failure(PosterRetentionMessages.DeleteFailed);
        }

        if (!_access.DirectoryExists(destination) || _access.DirectoryExists(source))
        {
            TryMove(destination, source);
            request.TemporaryDeletionAt = null;
            request.OriginalDirectory = null;
            request.PosterFile.StoragePath = previousPoster;
            if (request.ApprovalSheet is not null && previousApproval is not null)
            {
                request.ApprovalSheet.StoragePath = previousApproval;
            }

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _logger.LogWarning("Temporary deletion could not restore poster {PosterId}.", request.PosterId);
            }

            return RetentionOutcome.Failure(PosterRetentionMessages.DeleteFailed);
        }

        _logger.LogInformation(
            "Temporary deletion confirmed for poster {PosterId} by {ActorObjectId}.",
            request.PosterId,
            actor);
        _schedule.Wake();
        return RetentionOutcome.Success();
    }

    private async Task<RetentionOutcome> RestoreLockedAsync(
        string posterId,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken)
    {
        var request = await LoadAsync(posterId, cancellationToken);
        if (request is null)
        {
            return RetentionOutcome.Failure(PosterRetentionMessages.Missing);
        }

        if (request.PermanentlyDeletedAt is not null)
        {
            return RetentionOutcome.Failure(PosterRetentionMessages.Unavailable);
        }

        if (request.TemporaryDeletionAt is null || string.IsNullOrWhiteSpace(request.OriginalDirectory))
        {
            return RetentionOutcome.Failure(PosterRetentionMessages.NotDeleted);
        }

        if (AsUtc(request.TemporaryDeletionAt.Value).Add(PosterRetentionPaths.Retention) <= UtcNow())
        {
            await ExpireLockedAsync(request, cancellationToken);
            return RetentionOutcome.Failure(PosterRetentionMessages.Ended);
        }

        var source = PosterRetentionPaths.RetentionDirectory(request.OriginalDirectory);
        var destination = request.OriginalDirectory;
        if (_access.DirectoryExists(destination) || _access.FileExists(destination))
        {
            return RetentionOutcome.Failure(PosterRetentionMessages.RestoreConflict);
        }

        if (!TryMove(source, destination))
        {
            return RetentionOutcome.Failure(PosterRetentionMessages.RestoreFailed);
        }

        var previousPoster = request.PosterFile.StoragePath;
        var previousApproval = request.ApprovalSheet?.StoragePath;
        var previousDeletion = request.TemporaryDeletionAt;
        var restoredPoster = PrintFolderPaths.RewritePrefix(previousPoster, source, destination);
        var restoredApproval = previousApproval is null
            ? null
            : PrintFolderPaths.RewritePrefix(previousApproval, source, destination);
        if (restoredPoster is null || (previousApproval is not null && restoredApproval is null))
        {
            TryMove(destination, source);
            return RetentionOutcome.Failure(PosterRetentionMessages.RestoreFailed);
        }

        var now = UtcNow();
        var previousOriginal = request.OriginalDirectory;
        request.TemporaryDeletionAt = null;
        request.OriginalDirectory = null;
        request.PosterFile.StoragePath = restoredPoster;
        if (request.ApprovalSheet is not null)
        {
            request.ApprovalSheet.StoragePath = restoredApproval!;
        }

        var actor = ActorId(user);
        _db.PosterRetentionAudits.Add(Audit(request, PosterRetentionAction.Restoration, now, actor, destination));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            TryMove(destination, source);
            request.TemporaryDeletionAt = previousDeletion;
            request.OriginalDirectory = previousOriginal;
            request.PosterFile.StoragePath = previousPoster;
            if (request.ApprovalSheet is not null && previousApproval is not null)
            {
                request.ApprovalSheet.StoragePath = previousApproval;
            }

            DetachAddedAudits();
            return RetentionOutcome.Failure(PosterRetentionMessages.RestoreFailed);
        }

        _logger.LogInformation(
            "Poster {PosterId} restored by {ActorObjectId}.",
            request.PosterId,
            actor);
        _schedule.Wake();
        return RetentionOutcome.Success();
    }

    private async Task ExpireLockedAsync(PosterRequest request, CancellationToken cancellationToken)
    {
        if (request.PermanentlyDeletedAt is not null || request.TemporaryDeletionAt is null)
        {
            return;
        }

        var directory = string.IsNullOrWhiteSpace(request.OriginalDirectory)
            ? null
            : PosterRetentionPaths.RetentionDirectory(request.OriginalDirectory);
        if (directory is not null && _access.DirectoryExists(directory))
        {
            try
            {
                _access.DeleteDirectory(directory);
            }
            catch (IOException)
            {
                _logger.LogWarning("Permanent expiration could not delete poster {PosterId}.", request.PosterId);
                return;
            }
        }

        if (directory is not null && _access.DirectoryExists(directory))
        {
            _logger.LogWarning("Permanent expiration could not delete poster {PosterId}.", request.PosterId);
            return;
        }

        var now = UtcNow();
        request.PermanentlyDeletedAt = now;
        _db.PosterRetentionAudits.Add(Audit(
            request,
            PosterRetentionAction.PermanentExpiration,
            now,
            actor: null,
            request.OriginalDirectory ?? string.Empty));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            request.PermanentlyDeletedAt = null;
            DetachAddedAudits();
            _logger.LogWarning("Permanent expiration could not record poster {PosterId}.", request.PosterId);
            return;
        }

        _logger.LogInformation("Poster {PosterId} permanently deleted after retention.", request.PosterId);
    }

    private bool TryMove(string source, string destination)
    {
        try
        {
            if (!_access.DirectoryExists(source)
                || _access.IsReparsePoint(source)
                || _access.DirectoryExists(destination)
                || _access.FileExists(destination))
            {
                return false;
            }

            EnsureRetentionRoot();
            var separator = destination.LastIndexOf('/');
            if (separator <= 0)
            {
                return false;
            }

            var parent = destination[..separator];
            if (!_access.DirectoryExists(parent))
            {
                _access.CreateDirectory(parent);
            }

            _access.MoveDirectory(source, destination);
            return _access.DirectoryExists(destination) && !_access.DirectoryExists(source);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private void EnsureRetentionRoot()
    {
        if (!_access.DirectoryExists(PosterRetentionPaths.FolderName))
        {
            _access.CreateDirectory(PosterRetentionPaths.FolderName);
        }

        _access.HideDirectory(PosterRetentionPaths.FolderName);
    }

    private async Task<bool> IsTechnicianAsync(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return (await _authorization.AuthorizeAsync(user, PosterAccess.OperatorPolicy)).Succeeded;
    }

    private Guid? ActorId(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        var actor = _actors.FromPrincipal(user);
        return actor is { IsValid: true } ? actor.ObjectId : null;
    }

    private async Task<PosterRequest?> LoadAsync(string posterId, CancellationToken cancellationToken) =>
        await _db.PosterRequests
            .Include(request => request.PosterFile)
            .Include(request => request.ApprovalSheet)
            .SingleOrDefaultAsync(request => request.PosterId == posterId, cancellationToken);

    private void DetachAddedAudits()
    {
        foreach (var entry in _db.ChangeTracker.Entries<PosterRetentionAudit>()
                     .Where(candidate => candidate.State == EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static PosterRetentionAudit Audit(
        PosterRequest request,
        string action,
        DateTime occurredAt,
        Guid? actor,
        string originalDirectory) =>
        new()
        {
            PosterRequestId = request.PosterRequestId,
            PosterId = request.PosterId,
            Action = action,
            OccurredAt = occurredAt,
            ActorObjectId = actor,
            OriginalDirectory = originalDirectory
        };

    private static bool TryReadActiveDirectory(PosterRequest request, out string directory)
    {
        directory = string.Empty;
        var posterDirectory = PrintFolderPaths.DirectoryOf(request.PosterFile.StoragePath);
        if (posterDirectory is null || !IsPrintDirectory(posterDirectory))
        {
            return false;
        }

        if (request.ApprovalSheet is not null
            && !string.Equals(PrintFolderPaths.DirectoryOf(request.ApprovalSheet.StoragePath), posterDirectory, StringComparison.Ordinal))
        {
            return false;
        }

        var folder = posterDirectory[(posterDirectory.LastIndexOf('/') + 1)..];
        var expected = StorageNames.PosterFolder(request.Name, request.PosterId);
        if (expected is null || !string.Equals(folder, expected, StringComparison.Ordinal))
        {
            return false;
        }

        directory = posterDirectory;
        return true;
    }

    private static bool IsPrintDirectory(string directory)
    {
        var segments = directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment.Equals(PosterRetentionPaths.FolderName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (segments.Length == 3
            && segments[0] == StorageNames.Events
            && PrintFolderPaths.IsPrintEventFolder(segments[1])
            && StorageNames.PosterIdFromFolder(segments[2]) is not null)
        {
            return true;
        }

        return segments.Length == 3
            && PrintFolderPaths.IsWithoutPrintRoot(segments[0])
            && segments[1].Length == 4
            && StorageNames.PosterIdFromFolder(segments[2]) is not null;
    }

    private static bool TrySplitDirectory(string? directory, out string parent, out string folder)
    {
        parent = string.Empty;
        folder = string.Empty;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var separator = directory.LastIndexOf('/');
        if (separator <= 0 || separator == directory.Length - 1)
        {
            return false;
        }

        parent = directory[..separator];
        folder = directory[(separator + 1)..];
        return StorageNames.PosterIdFromFolder(folder) is not null;
    }
}
