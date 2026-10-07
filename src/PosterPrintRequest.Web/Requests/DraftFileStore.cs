using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Storage;

namespace PosterPrintRequest.Web.Requests;

public sealed class DraftFileStore : IDraftFileStore
{
    private readonly SharedStorageOptions _storage;

    public DraftFileStore(IOptions<SharedStorageOptions> storage)
    {
        _storage = storage.Value;
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

        DeleteRoleFiles(directory, role);
        var destinationPath = Path.Combine(directory, StoredName(role, selection.Format!));
        await using (var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true))
        {
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
            ? File.Exists(Path.Combine(directory, "poster.pdf")) || File.Exists(Path.Combine(directory, "poster.pptx"))
            : File.Exists(Path.Combine(directory, "approval-sheet.pdf"));
    }

    public string? PosterPath(string draftId)
    {
        if (!TryDraftDirectory(draftId, create: false, out var directory))
        {
            return null;
        }

        var pdf = Path.Combine(directory, "poster.pdf");
        if (File.Exists(pdf))
        {
            return pdf;
        }

        var pptx = Path.Combine(directory, "poster.pptx");
        return File.Exists(pptx) ? pptx : null;
    }

    public string? ApprovalSheetPath(string draftId)
    {
        if (!TryDraftDirectory(draftId, create: false, out var directory))
        {
            return null;
        }

        var path = Path.Combine(directory, "approval-sheet.pdf");
        return File.Exists(path) ? path : null;
    }

    public void DeleteDraft(string draftId)
    {
        if (TryDraftDirectory(draftId, create: false, out var directory))
        {
            DeleteDirectory(directory);
        }
    }

    private bool TryDraftDirectory(string draftId, bool create, out string directory) =>
        TryChildDirectory("drafts", draftId, create, out directory);

    private bool TryChildDirectory(string area, string key, bool create, out string directory)
    {
        directory = "";
        if (!DraftIds.IsValid(key) || string.IsNullOrWhiteSpace(_storage.RootPath))
        {
            return false;
        }

        var root = Path.GetFullPath(_storage.RootPath);
        directory = Path.GetFullPath(Path.Combine(root, area, key));
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            directory = "";
            return false;
        }

        if (create)
        {
            Directory.CreateDirectory(directory);
        }

        return !create || Directory.Exists(directory);
    }

    private static string StoredName(DraftFileRole role, string format) =>
        role == DraftFileRole.Poster
            ? format == "PPTX" ? "poster.pptx" : "poster.pdf"
            : "approval-sheet.pdf";

    private static void DeleteRoleFiles(string directory, DraftFileRole role)
    {
        if (role == DraftFileRole.Poster)
        {
            DeleteIfPresent(Path.Combine(directory, "poster.pdf"));
            DeleteIfPresent(Path.Combine(directory, "poster.pptx"));
            return;
        }

        DeleteIfPresent(Path.Combine(directory, "approval-sheet.pdf"));
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
