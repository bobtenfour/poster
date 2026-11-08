namespace PosterPrintRequest.Infrastructure.Storage;

public sealed record StorageEntry(string Name, bool IsDirectory);

/// <summary>
/// The only boundary for file-system operations under the shared-storage root.
/// Paths are relative to the root and use '/' separators. An empty path means the root,
/// where the operation allows it. Full physical paths and the root are never returned.
/// </summary>
public interface ISharedStorageAccess
{
    bool DirectoryExists(string relativePath);

    bool FileExists(string relativePath);

    void CreateDirectory(string relativePath);

    IReadOnlyList<StorageEntry> ListEntries(string relativePath);

    Stream OpenRead(string relativePath);

    Stream OpenWrite(string relativePath);

    void CopyFile(string sourceRelative, string destinationRelative);

    void MoveDirectory(string sourceRelative, string destinationRelative);

    void DeleteFile(string relativePath);

    void DeleteDirectory(string relativePath);

    long FileLength(string relativePath);

    bool IsReparsePoint(string relativePath);
}
