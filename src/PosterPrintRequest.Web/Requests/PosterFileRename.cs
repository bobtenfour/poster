using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Infrastructure.Storage;

namespace PosterPrintRequest.Web.Requests;

public sealed class PosterFileRenameResult
{
    public int Renamed { get; init; }

    public int Skipped { get; init; }
}

/// <summary>
/// Shared Storage Contract amendment of 25 November 2026: at startup, poster and Approval Sheet files stored
/// under the earlier names (Poster.pdf, Poster.pptx, Approval-Sheet.pdf) are renamed in place to
/// "&lt;Poster ID&gt; - &lt;requester name&gt; - &lt;Date In&gt;" names, and their StoragePath is updated.
/// The poster directory is not changed. The step is idempotent: a file already renamed is only recorded.
/// </summary>
public sealed class PosterFileRename
{
    private readonly PosterPrintRequestDbContext _db;
    private readonly ISharedStorageAccess _access;
    private readonly ILogger<PosterFileRename> _logger;

    public PosterFileRename(PosterPrintRequestDbContext db, ISharedStorageAccess access, ILogger<PosterFileRename> logger)
    {
        _db = db;
        _access = access;
        _logger = logger;
    }

    public async Task<PosterFileRenameResult> RenameAsync(CancellationToken cancellationToken)
    {
        var requests = await _db.PosterRequests
            .Include(request => request.PosterFile)
            .Include(request => request.ApprovalSheet)
            .Where(request => request.PermanentlyDeletedAt == null)
            .ToListAsync(cancellationToken);

        var renamed = 0;
        var skipped = 0;
        foreach (var request in requests)
        {
            var outcome = await RenameOneAsync(request, cancellationToken);
            if (outcome is true)
            {
                renamed++;
            }
            else if (outcome is false)
            {
                skipped++;
            }
        }

        if (renamed > 0 || skipped > 0)
        {
            _logger.LogInformation("Poster file rename renamed {Renamed} requests and skipped {Skipped}.", renamed, skipped);
        }

        return new PosterFileRenameResult { Renamed = renamed, Skipped = skipped };
    }

    /// <summary>True when renamed, false when it could not be renamed, null when there was nothing to rename.</summary>
    private async Task<bool?> RenameOneAsync(PosterRequest request, CancellationToken cancellationToken)
    {
        var poster = Plan(request.PosterFile.StoragePath, name => name is StorageNames.PosterPdf or StorageNames.PosterPptx,
            StorageNames.PosterFileName(request.PosterId, request.Name, request.DateIn, request.PosterFile.DetectedFormat));
        var approval = request.ApprovalSheet is null
            ? null
            : Plan(request.ApprovalSheet.StoragePath, name => name == StorageNames.ApprovalSheet,
                StorageNames.ApprovalSheetFileName(request.PosterId, request.Name, request.DateIn));

        if (poster is { Destination: null } || approval is { Destination: null })
        {
            return false;
        }

        if (poster is null && approval is null)
        {
            return null;
        }

        var copied = new List<string>();
        foreach (var step in new[] { poster, approval })
        {
            if (step is null)
            {
                continue;
            }

            if (!TryCopy(step.Source, step.Destination!, copied))
            {
                RemoveCopies(copied);
                return false;
            }
        }

        var previousPoster = request.PosterFile.StoragePath;
        var previousApproval = request.ApprovalSheet?.StoragePath;
        if (poster is not null)
        {
            request.PosterFile.StoragePath = poster.Destination!;
        }

        if (approval is not null)
        {
            request.ApprovalSheet!.StoragePath = approval.Destination!;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            request.PosterFile.StoragePath = previousPoster;
            if (request.ApprovalSheet is not null && previousApproval is not null)
            {
                request.ApprovalSheet.StoragePath = previousApproval;
            }

            RemoveCopies(copied);
            _logger.LogWarning("Poster file rename could not record poster {PosterId}.", request.PosterId);
            return false;
        }

        // The new names are recorded; the earlier files are removed only now.
        foreach (var step in new[] { poster, approval })
        {
            if (step is not null && _access.FileExists(step.Source) && _access.FileExists(step.Destination!))
            {
                try
                {
                    _access.DeleteFile(step.Source);
                }
                catch (IOException)
                {
                    _logger.LogWarning("Poster file rename left an earlier file for poster {PosterId}.", request.PosterId);
                }
            }
        }

        return true;
    }

    private static RenameStep? Plan(string storagePath, Func<string, bool> isEarlierName, string? newName)
    {
        var current = storagePath.Replace('\\', '/');
        var slash = current.LastIndexOf('/');
        if (slash <= 0 || !isEarlierName(current[(slash + 1)..]))
        {
            return null;
        }

        return new RenameStep(current, newName is null ? null : current[..slash] + "/" + newName);
    }

    private bool TryCopy(string source, string destination, List<string> copied)
    {
        try
        {
            if (_access.FileExists(destination))
            {
                // A previous run copied the file but did not record it.
                return true;
            }

            if (!_access.FileExists(source))
            {
                return false;
            }

            _access.CopyFile(source, destination);
            copied.Add(destination);
            return _access.FileExists(destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void RemoveCopies(IEnumerable<string> copied)
    {
        foreach (var path in copied)
        {
            try
            {
                if (_access.FileExists(path))
                {
                    _access.DeleteFile(path);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed record RenameStep(string Source, string? Destination);
}
