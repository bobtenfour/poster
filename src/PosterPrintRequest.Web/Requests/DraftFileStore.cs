using PosterPrintRequest.Infrastructure.Storage;

namespace PosterPrintRequest.Web.Requests;

public sealed class DraftFileStore : IDraftFileStore
{
    private readonly ISharedStorageAccess _access;

    public DraftFileStore(ISharedStorageAccess access)
    {
        _access = access;
    }

    public async Task<DraftSaveResult> SaveAsync(
        string draftId,
        DraftFileRole role,
        string originalFileName,
        Stream content,
        long size,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var selection = role == DraftFileRole.Poster
            ? FileSelectionRules.InspectPoster(originalFileName, size)
            : FileSelectionRules.InspectApprovalSheet(originalFileName, size);
        if (!selection.Succeeded)
        {
            return new DraftSaveResult(false, selection.Error, null);
        }

        if (!TryDraftDirectory(draftId, create: true, out var directory))
        {
            return new DraftSaveResult(false, "The file could not be stored. Choose it again.", null);
        }

        try
        {
            DeleteRoleFiles(directory, role);
            await using var destination = _access.OpenWrite(directory + "/" + StoredName(role, selection.Format!));
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                if (size > 0)
                {
                    progress?.Report((int)Math.Min(100, written * 100 / size));
                }
            }
        }
        catch (StorageFailureException)
        {
            return new DraftSaveResult(false, "The file could not be stored. Choose it again.", null);
        }

        progress?.Report(100);
        return new DraftSaveResult(true, null, selection.Format);
    }

    public void Remove(string draftId, DraftFileRole role)
    {
        if (TryDraftDirectory(draftId, create: false, out var directory))
        {
            DeleteRoleFiles(directory, role);
        }
    }

    public bool Exists(string draftId, DraftFileRole role)
    {
        if (!TryDraftDirectory(draftId, create: false, out var directory))
        {
            return false;
        }

        return role == DraftFileRole.Poster
            ? _access.FileExists(directory + "/poster.pdf") || _access.FileExists(directory + "/poster.pptx")
            : _access.FileExists(directory + "/approval-sheet.pdf");
    }

    /// <summary>The draft poster's path relative to the shared-storage root, or null.</summary>
    public string? PosterPath(string draftId)
    {
        if (!TryDraftDirectory(draftId, create: false, out var directory))
        {
            return null;
        }

        var pdf = directory + "/poster.pdf";
        if (_access.FileExists(pdf))
        {
            return pdf;
        }

        var pptx = directory + "/poster.pptx";
        return _access.FileExists(pptx) ? pptx : null;
    }

    /// <summary>The draft Approval Sheet's path relative to the shared-storage root, or null.</summary>
    public string? ApprovalSheetPath(string draftId)
    {
        if (!TryDraftDirectory(draftId, create: false, out var directory))
        {
            return null;
        }

        var path = directory + "/approval-sheet.pdf";
        return _access.FileExists(path) ? path : null;
    }

    public void DeleteDraft(string draftId)
    {
        if (TryDraftDirectory(draftId, create: false, out var directory) && _access.DirectoryExists(directory))
        {
            _access.DeleteDirectory(directory);
        }
    }

    private bool TryDraftDirectory(string draftId, bool create, out string directory)
    {
        directory = "";
        if (!DraftIds.IsValid(draftId))
        {
            return false;
        }

        directory = "drafts/" + draftId;
        if (!create)
        {
            return true;
        }

        try
        {
            _access.CreateDirectory(directory);
        }
        catch (StorageFailureException)
        {
            return false;
        }

        return _access.DirectoryExists(directory);
    }

    private static string StoredName(DraftFileRole role, string format) =>
        role == DraftFileRole.Poster
            ? format == "PPTX" ? "poster.pptx" : "poster.pdf"
            : "approval-sheet.pdf";

    private void DeleteRoleFiles(string directory, DraftFileRole role)
    {
        if (role == DraftFileRole.Poster)
        {
            DeleteIfPresent(directory + "/poster.pdf");
            DeleteIfPresent(directory + "/poster.pptx");
            return;
        }

        DeleteIfPresent(directory + "/approval-sheet.pdf");
    }

    private void DeleteIfPresent(string path)
    {
        if (_access.FileExists(path))
        {
            _access.DeleteFile(path);
        }
    }
}
