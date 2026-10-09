using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web.Requests;

public sealed class PrintFolderMigrationResult
{
    public int Moved { get; init; }

    public int Incomplete { get; init; }
}

public sealed class PrintFolderMigration
{
    private readonly PosterPrintRequestDbContext _db;
    private readonly AcceptedStorage _storage;
    private readonly ILogger<PrintFolderMigration> _logger;

    public PrintFolderMigration(
        PosterPrintRequestDbContext db,
        AcceptedStorage storage,
        ILogger<PrintFolderMigration> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    public async Task<PrintFolderMigrationResult> MigrateAsync(CancellationToken cancellationToken)
    {
        var requests = await _db.PosterRequests
            .Include(request => request.PosterFile)
            .Include(request => request.ApprovalSheet)
            .Include(request => request.PosterProcessing)
            .ToListAsync(cancellationToken);

        var moved = 0;
        var incomplete = 0;
        foreach (var request in requests)
        {
            if (await MigrateOneAsync(request, cancellationToken))
            {
                moved++;
            }
            else if (PrintFolderPaths.TryMapAcceptedFile(request.PosterFile.StoragePath, request.PosterProcessing.Printed, out _))
            {
                incomplete++;
            }
        }

        if (moved > 0 || incomplete > 0)
        {
            _logger.LogInformation(
                "Accepted layout migration moved {Moved} poster directories and left {Incomplete} incomplete.",
                moved,
                incomplete);
        }

        return new PrintFolderMigrationResult { Moved = moved, Incomplete = incomplete };
    }

    private async Task<bool> MigrateOneAsync(PosterRequest request, CancellationToken cancellationToken)
    {
        if (!PrintFolderPaths.TryMapAcceptedFile(request.PosterFile.StoragePath, request.PosterProcessing.Printed, out var destinationFile))
        {
            return false;
        }

        var sourceDirectory = PrintFolderPaths.DirectoryOf(request.PosterFile.StoragePath);
        var destinationDirectory = PrintFolderPaths.DirectoryOf(destinationFile);
        if (sourceDirectory is null || destinationDirectory is null)
        {
            return false;
        }

        string? destinationApproval = null;
        if (request.ApprovalSheet is not null)
        {
            destinationApproval = PrintFolderPaths.RewritePrefix(request.ApprovalSheet.StoragePath, sourceDirectory, destinationDirectory);
            if (destinationApproval is null)
            {
                return false;
            }
        }

        var printed = request.PosterProcessing.Printed;
        var posterId = request.PosterId;
        var reasonId = request.ReasonId;
        if (!_storage.DirectorySettled(sourceDirectory, destinationDirectory, destinationFile, destinationApproval)
            && !_storage.TryMoveDirectory(sourceDirectory, destinationDirectory))
        {
            return false;
        }

        var previousPoster = request.PosterFile.StoragePath;
        var previousApproval = request.ApprovalSheet?.StoragePath;
        request.PosterFile.StoragePath = destinationFile;
        if (request.ApprovalSheet is not null)
        {
            request.ApprovalSheet.StoragePath = destinationApproval!;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _storage.TryMoveDirectory(destinationDirectory, sourceDirectory);
            request.PosterFile.StoragePath = previousPoster;
            if (request.ApprovalSheet is not null && previousApproval is not null)
            {
                request.ApprovalSheet.StoragePath = previousApproval;
            }

            return false;
        }

        var complete = request.PosterProcessing.Printed == printed
            && request.PosterId == posterId
            && request.ReasonId == reasonId
            && _storage.DirectorySettled(sourceDirectory, destinationDirectory, destinationFile, destinationApproval);
        if (complete)
        {
            return true;
        }

        _storage.TryMoveDirectory(destinationDirectory, sourceDirectory);
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
            _logger.LogWarning("Accepted layout migration could not restore poster {PosterId}.", request.PosterId);
        }

        return false;
    }
}
