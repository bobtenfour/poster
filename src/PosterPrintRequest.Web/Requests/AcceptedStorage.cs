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
    private readonly ISharedStorageAccess _access;
    private readonly DraftFileStore _drafts;

    public AcceptedStorage(ISharedStorageAccess access, DraftFileStore drafts)
    {
        _access = access;
        _drafts = drafts;
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

        var posterSource = _drafts.PosterPath(draftId);
        if (posterSource is null)
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
            if (_access.DirectoryExists(relativeDirectory))
            {
                _access.DeleteDirectory(relativeDirectory);
            }

            _access.CreateDirectory(relativeDirectory);
            _access.CopyFile(posterSource, posterRelative);
            if (approvalSource is not null)
            {
                _access.CopyFile(approvalSource, approvalRelative!);
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
        var source = Normalize(sourceRelative);
        var destination = Normalize(destinationRelative);
        if (source is null || destination is null || string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            if (!_access.DirectoryExists(source)
                || _access.IsReparsePoint(source)
                || _access.DirectoryExists(destination)
                || _access.FileExists(destination))
            {
                return false;
            }

            var separator = destination.LastIndexOf('/');
            if (separator <= 0)
            {
                return false;
            }

            _access.CreateDirectory(destination[..separator]);
            _access.MoveDirectory(source, destination);

            return !_access.DirectoryExists(source)
                && _access.DirectoryExists(destination)
                && !_access.IsReparsePoint(destination);
        }
        catch (IOException)
        {
            return false;
        }
    }

    public bool DirectorySettled(string sourceDirectory, string destinationDirectory, string posterFile, string? approvalFile)
    {
        var source = Normalize(sourceDirectory);
        var destination = Normalize(destinationDirectory);
        var poster = Normalize(posterFile);
        if (source is null || destination is null || poster is null)
        {
            return false;
        }

        if (_access.DirectoryExists(source) || !_access.DirectoryExists(destination) || !_access.FileExists(poster))
        {
            return false;
        }

        if (approvalFile is null)
        {
            return true;
        }

        var approval = Normalize(approvalFile);
        return approval is not null
            && _access.FileExists(approval)
            && !_access.FileExists(source + "/" + approval[(approval.LastIndexOf('/') + 1)..]);
    }

    public void DeletePlacement(string? directoryRelative)
    {
        var directory = Normalize(directoryRelative);
        if (directory is null)
        {
            return;
        }

        try
        {
            if (_access.DirectoryExists(directory))
            {
                _access.DeleteDirectory(directory);
            }
        }
        catch (IOException)
        {
        }
    }

    private static string? Normalize(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalized = relativePath.Replace('\\', '/').Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
