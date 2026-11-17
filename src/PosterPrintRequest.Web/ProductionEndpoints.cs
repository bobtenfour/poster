using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web;

public static class ProductionHealth
{
    public static async Task<bool> IsReadyAsync(
        PosterPrintRequestDbContext db,
        ISharedStorageAccess storage,
        CancellationToken cancellationToken)
    {
        var databaseReady = false;
        try
        {
            databaseReady = await db.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception)
        {
            databaseReady = false;
        }

        var storageReady = storage.DirectoryExists("");
        return databaseReady && storageReady;
    }
}

public static class ProductionEndpoints
{
    public static void MapProductionEndpoints(this WebApplication app)
    {
        app.MapGet("/health", Ready).AllowAnonymous();
        app.MapGet("/health/ready", Ready).AllowAnonymous();

        app.MapGet("/technician/files/{posterId}/poster", (string posterId, bool? inline, PosterPrintRequestDbContext db, ISharedStorageAccess storage, CancellationToken cancellationToken) =>
            SendFileAsync(posterId, approval: false, inline == true, db, storage, cancellationToken))
            .RequireAuthorization(PosterAccess.OperatorPolicy);

        app.MapGet("/technician/files/{posterId}/approval", (string posterId, bool? inline, PosterPrintRequestDbContext db, ISharedStorageAccess storage, CancellationToken cancellationToken) =>
            SendFileAsync(posterId, approval: true, inline == true, db, storage, cancellationToken))
            .RequireAuthorization(PosterAccess.OperatorPolicy);

        // TDX Integration Contract section 17.11: replaced files are history, shown to technicians only.
        app.MapGet("/technician/files/" + ReplacementNames.Root + "/{folder}/{file}", (string folder, string file, bool? inline, ISharedStorageAccess storage) =>
            SendArchivedFile(folder, file, inline == true, storage))
            .RequireAuthorization(PosterAccess.OperatorPolicy);
    }

    private static IResult SendArchivedFile(string folder, string file, bool inline, ISharedStorageAccess storage)
    {
        if (!ReplacementNames.IsArchiveFolder(folder) || !ReplacementNames.IsArchiveFile(file))
        {
            return Results.NotFound();
        }

        var relative = ReplacementNames.Root + "/" + folder + "/" + file;
        if (storage.IsReparsePoint(ReplacementNames.Root)
            || storage.IsReparsePoint(ReplacementNames.Root + "/" + folder)
            || !storage.FileExists(relative))
        {
            return Results.NotFound();
        }

        Stream content;
        try
        {
            content = storage.OpenRead(relative);
        }
        catch (StorageFailureException)
        {
            return Results.NotFound();
        }

        var contentType = file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : file.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.openxmlformats-officedocument.presentationml.presentation"
                : "text/plain; charset=utf-8";
        return inline
            ? Results.File(content, contentType, enableRangeProcessing: true)
            : Results.File(content, contentType, file, enableRangeProcessing: true);
    }

    private static async Task<IResult> Ready(
        PosterPrintRequestDbContext db,
        ISharedStorageAccess storage,
        CancellationToken cancellationToken)
    {
        var ready = await ProductionHealth.IsReadyAsync(db, storage, cancellationToken);
        return Results.Json(new { status = ready ? "Healthy" : "Unhealthy" }, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> SendFileAsync(
        string posterId,
        bool approval,
        bool inline,
        PosterPrintRequestDbContext db,
        ISharedStorageAccess storage,
        CancellationToken cancellationToken)
    {
        if (!PosterIds.IsPublic(posterId))
        {
            return Results.NotFound();
        }

        var request = await db.PosterRequests
            .AsNoTracking()
            .Include(item => item.PosterFile)
            .Include(item => item.ApprovalSheet)
            .SingleOrDefaultAsync(item => item.PosterId == posterId, cancellationToken);
        if (request is null)
        {
            return Results.NotFound();
        }

        var relative = approval ? request.ApprovalSheet?.StoragePath : request.PosterFile.StoragePath;
        if (string.IsNullOrWhiteSpace(relative) || !storage.FileExists(relative))
        {
            return Results.NotFound();
        }

        Stream content;
        try
        {
            content = storage.OpenRead(relative);
        }
        catch (StorageFailureException)
        {
            return Results.NotFound();
        }

        var downloadName = relative[(relative.LastIndexOf('/') + 1)..];
        var contentType = downloadName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : downloadName.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.openxmlformats-officedocument.presentationml.presentation"
                : "application/octet-stream";
        return inline
            ? Results.File(content, contentType, enableRangeProcessing: true)
            : Results.File(content, contentType, downloadName, enableRangeProcessing: true);
    }
}
