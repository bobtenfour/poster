using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Storage;

namespace PosterPrintRequest.Web.Requests;

public sealed class AcceptedPlacement
{
    public required string DirectoryRelative { get; init; }

    public required string PosterRelative { get; init; }

    public string? ApprovalRelative { get; init; }
}

public sealed class AcceptedStorage
{
    private readonly SharedStorageOptions _storage;
    private readonly DraftFileStore _drafts;

    public AcceptedStorage(IOptions<SharedStorageOptions> storage, DraftFileStore drafts)
    {
        _storage = storage.Value;
        _drafts = drafts;
    }

    public string? Resolve(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(_storage.RootPath))
        {
            return null;
        }

        var normalized = relativePath.Replace('\\', '/').Trim();
        if (normalized.StartsWith('/') || normalized.Contains(':'))
        {
            return null;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0
            || segments.Any(segment => segment is "." or ".." || segment.Contains("..", StringComparison.Ordinal)))
        {
            return null;
        }

        var root = Path.GetFullPath(_storage.RootPath);
        var combined = root;
        foreach (var segment in segments)
        {
            combined = Path.Combine(combined, segment);
        }

        var full = Path.GetFullPath(combined);
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return full;
    }

    public bool TryPlace(
        string draftId,
        string posterId,
        string? eventName,
        DateTime dateIn,
        string requesterName,
        bool includeApproval,
        string detectedFormat,
        out AcceptedPlacement? placement,
        out string? error)
    {
        placement = null;
        error = null;
        if (!PosterIds.IsPublic(posterId))
        {
            error = "The request could not be accepted. Submit it again.";
            return false;
        }

        string relativeDirectory;
        var posterFolder = StorageNames.PosterFolder(requesterName, posterId);
        if (posterFolder is null)
        {
            error = "The request could not be accepted. Submit it again.";
            return false;
        }

        var storeApproval = includeApproval && !string.IsNullOrWhiteSpace(eventName);
        if (string.IsNullOrWhiteSpace(eventName))
        {
            relativeDirectory = $"{PrintFolderPaths.WithoutEventToBePrinted}/{StorageNames.Year(dateIn)}/{posterFolder}";
        }
        else
        {
            var eventFolder = StorageNames.EventFolder(eventName, dateIn);
            if (eventFolder is null)
            {
                error = "The request could not be accepted. Submit it again.";
                return false;
            }

            relativeDirectory = $"{StorageNames.Events}/{eventFolder}{PrintFolderPaths.ToBePrintedSuffix}/{posterFolder}";
        }

        var directoryFull = Resolve(relativeDirectory);
        var posterSource = _drafts.PosterPath(draftId);
        if (directoryFull is null || posterSource is null)
        {
            error = "The files could not be stored. Choose them again.";
            return false;
        }

        string? approvalSource = null;
        if (storeApproval)
        {
            approvalSource = _drafts.ApprovalSheetPath(draftId);
            if (approvalSource is null)
            {
                error = "Choose the Approval Sheet PDF for this event.";
                return false;
            }
        }

        var posterName = StorageNames.PosterFileName(detectedFormat);
        var posterRelative = $"{relativeDirectory}/{posterName}";
        var approvalRelative = storeApproval ? $"{relativeDirectory}/{StorageNames.ApprovalSheet}" : null;

        try
        {
            if (Directory.Exists(directoryFull))
            {
                Directory.Delete(directoryFull, recursive: true);
            }

            Directory.CreateDirectory(directoryFull);
            File.Copy(posterSource, Path.Combine(directoryFull, posterName), overwrite: false);
            if (approvalSource is not null)
            {
                File.Copy(approvalSource, Path.Combine(directoryFull, StorageNames.ApprovalSheet), overwrite: false);
            }
        }
        catch (IOException)
        {
            DeletePlacement(relativeDirectory);
            error = "The files could not be stored. Choose them again.";
            return false;
        }

        placement = new AcceptedPlacement
        {
            DirectoryRelative = relativeDirectory,
            PosterRelative = posterRelative,
            ApprovalRelative = approvalRelative
        };
        return true;
    }

    public bool TryMoveDirectory(string? sourceRelative, string? destinationRelative)
    {
        var source = Resolve(sourceRelative);
        var destination = Resolve(destinationRelative);
        if (source is null || destination is null || string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Directory.Exists(source) || IsReparsePoint(source) || Directory.Exists(destination) || File.Exists(destination))
        {
            return false;
        }

        var parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parent))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(parent);
            Directory.Move(source, destination);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return !Directory.Exists(source) && Directory.Exists(destination) && !IsReparsePoint(destination);
    }

    public bool DirectorySettled(string sourceDirectory, string destinationDirectory, string posterFile, string? approvalFile)
    {
        var source = Resolve(sourceDirectory);
        var destination = Resolve(destinationDirectory);
        var poster = Resolve(posterFile);
        if (source is null || destination is null || poster is null)
        {
            return false;
        }

        if (Directory.Exists(source) || !Directory.Exists(destination) || !File.Exists(poster))
        {
            return false;
        }

        if (approvalFile is null)
        {
            return true;
        }

        var approval = Resolve(approvalFile);
        return approval is not null
            && File.Exists(approval)
            && !File.Exists(Path.Combine(source, Path.GetFileName(approval)));
    }

    public void DeletePlacement(string? directoryRelative)
    {
        var full = Resolve(directoryRelative);
        if (full is null || !Directory.Exists(full))
        {
            return;
        }

        try
        {
            Directory.Delete(full, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
