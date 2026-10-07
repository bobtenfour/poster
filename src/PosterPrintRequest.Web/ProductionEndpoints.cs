using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Persistence;
using PosterPrintRequest.Infrastructure.Storage;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web;

public static class ProductionHealth
{
    public static async Task<bool> IsReadyAsync(
        PosterPrintRequestDbContext db,
        string? rootPath,
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

        var storageReady = !string.IsNullOrWhiteSpace(rootPath) && Directory.Exists(rootPath);
        return databaseReady && storageReady;
    }
}

public static class ProductionEndpoints
{
    public static void MapProductionEndpoints(this WebApplication app)
    {
        app.MapGet("/health", async (PosterPrintRequestDbContext db, IOptions<SharedStorageOptions> storage, CancellationToken cancellationToken) =>
        {
            var ready = await ProductionHealth.IsReadyAsync(db, storage.Value.RootPath, cancellationToken);
            return Results.Json(new { status = ready ? "Healthy" : "Unhealthy" }, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });

        app.MapGet("/technician/files/{posterId}/poster", (string posterId, bool? inline, PosterPrintRequestDbContext db, AcceptedStorage storage, CancellationToken cancellationToken) =>
            SendFileAsync(posterId, approval: false, inline == true, db, storage, cancellationToken))
            .RequireAuthorization(PosterAccess.OperatorPolicy);

        app.MapGet("/technician/files/{posterId}/approval", (string posterId, bool? inline, PosterPrintRequestDbContext db, AcceptedStorage storage, CancellationToken cancellationToken) =>
            SendFileAsync(posterId, approval: true, inline == true, db, storage, cancellationToken))
            .RequireAuthorization(PosterAccess.OperatorPolicy);
    }

    private static async Task<IResult> SendFileAsync(
        string posterId,
        bool approval,
        bool inline,
        PosterPrintRequestDbContext db,
        AcceptedStorage storage,
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
        var full = storage.Resolve(relative);
        if (full is null || !File.Exists(full))
        {
            return Results.NotFound();
        }

        var downloadName = Path.GetFileName(full);
        var contentType = downloadName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : downloadName.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.openxmlformats-officedocument.presentationml.presentation"
                : "application/octet-stream";
        return inline
            ? Results.File(full, contentType)
            : Results.File(full, contentType, downloadName);
    }
}
