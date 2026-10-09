using System.Globalization;
using Microsoft.Extensions.Options;
using PosterPrintRequest.Infrastructure.Storage;

namespace PosterPrintRequest.Web.Requests;

public sealed class LibraryListing
{
    public required bool Available { get; init; }

    public required IReadOnlyList<string> Segments { get; init; }

    public required IReadOnlyList<LibraryEntry> Entries { get; init; }
}

public sealed class LibraryEntry
{
    public required string Name { get; init; }

    public required bool Directory { get; init; }

    public required string Kind { get; init; }

    public string? NavigateHref { get; init; }

    public string? WorkHref { get; init; }

    public string? ViewHref { get; init; }

    public string? Tone { get; init; }
}

public interface ITechnicianLibrary
{
    LibraryListing? List(string? relativePath);
}

public sealed class TechnicianLibrary : ITechnicianLibrary
{
    private readonly SharedStorageOptions _storage;
    private readonly AcceptedStorage _accepted;

    public TechnicianLibrary(IOptions<SharedStorageOptions> storage, AcceptedStorage accepted)
    {
        _storage = storage.Value;
        _accepted = accepted;
    }

    public LibraryListing? List(string? relativePath)
    {
        var root = NormalizedRoot();
        if (root is null)
        {
            return null;
        }

        var requested = string.IsNullOrWhiteSpace(relativePath) ? "" : relativePath.Trim().Trim('/');
        if (!Directory.Exists(root))
        {
            return requested.Length == 0
                ? new LibraryListing { Available = false, Segments = [], Entries = [] }
                : null;
        }

        IReadOnlyList<string> segments;
        string full;
        if (requested.Length == 0)
        {
            return RootListing();
        }

        segments = requested.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!IsAllowedLocation(segments))
        {
            return null;
        }

        var physical = PhysicalSegments(segments);
        var resolved = _accepted.Resolve(string.Join('/', physical));
        if (resolved is null || !Directory.Exists(resolved) || !IsInside(root, resolved) || !Reconstructs(root, resolved, physical))
        {
            return segments.Count == 1
                ? new LibraryListing { Available = true, Segments = segments, Entries = [] }
                : null;
        }

        full = resolved;

        var entries = new List<LibraryEntry>();
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateFileSystemEntries(full);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var child in children)
        {
            if (IsReparsePoint(child))
            {
                continue;
            }

            var name = Path.GetFileName(child);
            var directory = Directory.Exists(child);
            if (string.IsNullOrEmpty(name) || !IsAllowedChild(segments, name, directory))
            {
                continue;
            }

            var childPhysical = physical.Append(name).ToArray();
            var childFull = _accepted.Resolve(string.Join('/', childPhysical));
            if (childFull is null || !IsInside(root, childFull) || !Reconstructs(root, childFull, childPhysical))
            {
                continue;
            }

            entries.Add(new LibraryEntry
            {
                Name = name,
                Directory = directory,
                Kind = Kind(segments, name, directory),
                Tone = directory ? PrintFolderPaths.Tone(name) : null,
                NavigateHref = directory ? LibraryHref(segments.Append(name)) : null,
                WorkHref = directory && StorageNames.PosterIdFromFolder(name) is { } posterId
                    ? "/technician/work/" + posterId
                    : null,
                ViewHref = ViewHref(segments, name, directory)
            });
        }

        return new LibraryListing
        {
            Available = true,
            Segments = segments,
            Entries = entries
                .OrderBy(entry => entry.Directory ? 0 : 1)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    public static string LibraryHref(IEnumerable<string> segments)
    {
        var encoded = segments.Select(Uri.EscapeDataString).ToArray();
        return encoded.Length == 0
            ? "/technician/library"
            : "/technician/library/" + string.Join("/", encoded);
    }

    private static LibraryListing RootListing() =>
        new()
        {
            Available = true,
            Segments = [],
            Entries =
            [
                Folder("TO BE PRINTED", "Events", PrintFolderPaths.ToBePrintedTone, [PrintFolderPaths.EventsToBePrinted]),
                Folder("PRINTED", "Events", PrintFolderPaths.PrintedTone, [PrintFolderPaths.EventsPrinted]),
                Folder("TO BE PRINTED", "Without event", PrintFolderPaths.ToBePrintedTone, [PrintFolderPaths.WithoutEventToBePrinted]),
                Folder("PRINTED", "Without event", PrintFolderPaths.PrintedTone, [PrintFolderPaths.WithoutEventPrinted])
            ]
        };

    private static LibraryEntry Folder(string name, string kind, string tone, IReadOnlyList<string> hrefSegments) =>
        new()
        {
            Name = name,
            Directory = true,
            Kind = kind,
            Tone = tone,
            NavigateHref = LibraryHref(hrefSegments)
        };

    private static string[] PhysicalSegments(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0 || !PrintFolderPaths.IsVirtualEventsRoot(segments[0]))
        {
            return segments.ToArray();
        }

        var physical = new string[segments.Count];
        physical[0] = StorageNames.Events;
        for (var index = 1; index < segments.Count; index++)
        {
            physical[index] = segments[index];
        }

        return physical;
    }

    private string? NormalizedRoot()
    {
        if (string.IsNullOrWhiteSpace(_storage.RootPath))
        {
            return null;
        }

        return Path.GetFullPath(_storage.RootPath).TrimEnd(Path.DirectorySeparatorChar);
    }

    private static bool IsInside(string root, string full)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Reconstructs(string root, string full, IReadOnlyList<string> segments)
    {
        var current = root;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if (IsReparsePoint(current))
            {
                return false;
            }
        }

        return string.Equals(Path.GetFullPath(current), Path.GetFullPath(full), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedLocation(IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
        {
            return true;
        }

        if (segments.Any(IsReserved))
        {
            return false;
        }

        if (segments.Count == 1)
        {
            return segments[0] == StorageNames.Events
                || PrintFolderPaths.IsVirtualEventsRoot(segments[0])
                || PrintFolderPaths.IsWithoutPrintRoot(segments[0]);
        }

        if (PrintFolderPaths.IsVirtualEventsRoot(segments[0]))
        {
            if (!MatchesVirtualEvents(segments[0], segments[1]))
            {
                return false;
            }

            return segments.Count == 2
                || (segments.Count == 3 && StorageNames.PosterIdFromFolder(segments[2]) is not null);
        }

        if (segments[0] == StorageNames.Events)
        {
            if (!PrintFolderPaths.IsPrintEventFolder(segments[1]))
            {
                return false;
            }

            return segments.Count == 2
                || (segments.Count == 3 && StorageNames.PosterIdFromFolder(segments[2]) is not null);
        }

        if (!PrintFolderPaths.IsWithoutPrintRoot(segments[0]))
        {
            return false;
        }

        if (segments.Count == 1)
        {
            return true;
        }

        if (!IsYear(segments[1]))
        {
            return false;
        }

        return segments.Count == 2
            || (segments.Count == 3 && StorageNames.PosterIdFromFolder(segments[2]) is not null);
    }

    private static bool IsAllowedChild(IReadOnlyList<string> parent, string name, bool directory)
    {
        if (name is "." or ".." || name.Contains("..", StringComparison.Ordinal) || IsReserved(name))
        {
            return false;
        }

        if (parent.Count == 0)
        {
            return directory && (name == StorageNames.Events || PrintFolderPaths.IsWithoutPrintRoot(name));
        }

        if (parent.Count == 1 && parent[0] == StorageNames.Events)
        {
            return directory && PrintFolderPaths.IsPrintEventFolder(name);
        }

        if (parent.Count == 1 && PrintFolderPaths.IsVirtualEventsRoot(parent[0]))
        {
            return directory && MatchesVirtualEvents(parent[0], name);
        }

        if (parent.Count == 1 && PrintFolderPaths.IsWithoutPrintRoot(parent[0]))
        {
            return directory && IsYear(name);
        }

        if (parent.Count == 2 && IsEventLibraryRoot(parent[0]) && PrintFolderPaths.IsPrintEventFolder(parent[1]))
        {
            return directory && StorageNames.PosterIdFromFolder(name) is not null;
        }

        if (parent.Count == 2 && PrintFolderPaths.IsWithoutPrintRoot(parent[0]) && IsYear(parent[1]))
        {
            return directory && StorageNames.PosterIdFromFolder(name) is not null;
        }

        if (IsPosterLocation(parent))
        {
            return !directory && IsVisibleFile(parent, name);
        }

        return false;
    }

    private static bool IsPosterLocation(IReadOnlyList<string> segments) =>
        segments.Count == 3 && StorageNames.PosterIdFromFolder(segments[2]) is not null && IsAllowedLocation(segments);

    private static bool IsVisibleFile(IReadOnlyList<string> parent, string name)
    {
        if (name is StorageNames.PosterPdf or StorageNames.PosterPptx)
        {
            return true;
        }

        return parent.Count > 0 && IsEventLibraryRoot(parent[0]) && name == StorageNames.ApprovalSheet;
    }

    private static string Kind(IReadOnlyList<string> parent, string name, bool directory)
    {
        if (!directory)
        {
            if (name is StorageNames.PosterPdf or StorageNames.PosterPptx)
            {
                return "Poster file";
            }

            return name == StorageNames.ApprovalSheet ? "Approval sheet" : "File";
        }

        if (name == StorageNames.Events)
        {
            return "Events";
        }

        if (PrintFolderPaths.IsWithoutPrintRoot(name))
        {
            return "Without event";
        }

        if (StorageNames.PosterIdFromFolder(name) is not null)
        {
            return "Poster";
        }

        return IsYear(name) && parent.Count == 1 && PrintFolderPaths.IsWithoutPrintRoot(parent[0]) ? "Year" : "Event";
    }

    private static string? ViewHref(IReadOnlyList<string> parent, string name, bool directory)
    {
        if (directory || !IsPosterLocation(parent))
        {
            return null;
        }

        var posterId = StorageNames.PosterIdFromFolder(parent[^1]);
        if (posterId is null)
        {
            return null;
        }

        if (name is StorageNames.PosterPdf or StorageNames.PosterPptx)
        {
            return "/technician/files/" + posterId + "/poster?inline=true";
        }

        return name == StorageNames.ApprovalSheet
            ? "/technician/files/" + posterId + "/approval?inline=true"
            : null;
    }

    private static bool MatchesVirtualEvents(string virtualRoot, string name)
    {
        if (!PrintFolderPaths.IsPrintEventFolder(name))
        {
            return false;
        }

        var toBePrinted = name.EndsWith(PrintFolderPaths.ToBePrintedSuffix, StringComparison.Ordinal);
        return virtualRoot == PrintFolderPaths.EventsToBePrinted
            ? toBePrinted
            : !toBePrinted;
    }

    private static bool IsEventLibraryRoot(string name) =>
        name == StorageNames.Events || PrintFolderPaths.IsVirtualEventsRoot(name);

    private static bool IsReserved(string name) =>
        name.Equals("drafts", StringComparison.OrdinalIgnoreCase)
        || name.Equals("requests", StringComparison.OrdinalIgnoreCase);

    private static bool IsYear(string name) =>
        name.Length == 4
        && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
        && year is >= 1900 and <= 9999;

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
