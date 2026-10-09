using System.Globalization;

namespace PosterPrintRequest.Web.Requests;

public static class PrintFolderPaths
{
    public const string ToBePrintedSuffix = " TO BE PRINTED";

    public const string PrintedSuffix = " PRINTED";

    public const string ToBePrintedTone = "to-be-printed";

    public const string PrintedTone = "printed";

    public static string WithoutEventToBePrinted => StorageNames.WithoutEvent + ToBePrintedSuffix;

    public static string WithoutEventPrinted => StorageNames.WithoutEvent + PrintedSuffix;

    public static string EventsToBePrinted => StorageNames.Events + ToBePrintedSuffix;

    public static string EventsPrinted => StorageNames.Events + PrintedSuffix;

    public static bool IsVirtualEventsRoot(string name) =>
        name == EventsToBePrinted || name == EventsPrinted;

    public static string? Tone(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (name.EndsWith(ToBePrintedSuffix, StringComparison.Ordinal))
        {
            return ToBePrintedTone;
        }

        return name.EndsWith(PrintedSuffix, StringComparison.Ordinal) ? PrintedTone : null;
    }

    public static bool TryMapAcceptedFile(string? relativeFile, bool printed, out string destinationFile)
    {
        destinationFile = string.Empty;
        if (!TrySplit(relativeFile, out var segments) || segments.Length < 4)
        {
            return false;
        }

        if (segments[0] == StorageNames.Events)
        {
            if (segments[1].EndsWith(ToBePrintedSuffix, StringComparison.Ordinal)
                || segments[1].EndsWith(PrintedSuffix, StringComparison.Ordinal))
            {
                return false;
            }

            segments[1] += printed ? PrintedSuffix : ToBePrintedSuffix;
        }
        else if (segments[0] == StorageNames.WithoutEvent)
        {
            if (!IsYear(segments[1]))
            {
                return false;
            }

            segments[0] = printed ? WithoutEventPrinted : WithoutEventToBePrinted;
        }
        else
        {
            return false;
        }

        destinationFile = string.Join('/', segments);
        return true;
    }

    public static bool TryMapPrintedFile(string? relativeFile, out string destinationFile)
    {
        destinationFile = string.Empty;
        if (!TrySplit(relativeFile, out var segments) || segments.Length < 4)
        {
            return false;
        }

        var index = segments[0] == StorageNames.Events && segments[1].EndsWith(ToBePrintedSuffix, StringComparison.Ordinal)
            ? 1
            : segments[0] == WithoutEventToBePrinted
                ? 0
                : -1;
        if (index < 0)
        {
            return false;
        }

        segments[index] = segments[index][..^ToBePrintedSuffix.Length] + PrintedSuffix;
        destinationFile = string.Join('/', segments);
        return true;
    }

    public static string? DirectoryOf(string? relativeFile)
    {
        if (!TrySplit(relativeFile, out var segments) || segments.Length < 2)
        {
            return null;
        }

        return string.Join('/', segments[..^1]);
    }

    public static string? RewritePrefix(string? relativePath, string? sourceDirectory, string? destinationDirectory)
    {
        if (!TrySplit(relativePath, out var path) || !TrySplit(sourceDirectory, out var source) || !TrySplit(destinationDirectory, out var destination))
        {
            return null;
        }

        var file = string.Join('/', path);
        var from = string.Join('/', source);
        var to = string.Join('/', destination);
        if (!file.StartsWith(from + "/", StringComparison.Ordinal))
        {
            return null;
        }

        return to + file[from.Length..];
    }

    public static bool IsPrintEventFolder(string name)
    {
        if (name.EndsWith(ToBePrintedSuffix, StringComparison.Ordinal))
        {
            return IsEventYearFolder(name[..^ToBePrintedSuffix.Length]);
        }

        if (name.EndsWith(PrintedSuffix, StringComparison.Ordinal))
        {
            return IsEventYearFolder(name[..^PrintedSuffix.Length]);
        }

        return false;
    }

    public static bool IsWithoutPrintRoot(string name) =>
        name == WithoutEventToBePrinted || name == WithoutEventPrinted;

    private static bool IsEventYearFolder(string name)
    {
        var segment = StorageNames.Segment(name);
        if (segment is null || !string.Equals(segment, name, StringComparison.Ordinal))
        {
            return false;
        }

        var space = name.LastIndexOf(' ');
        return space > 0 && IsYear(name[(space + 1)..]);
    }

    private static bool IsYear(string name) =>
        name.Length == 4
        && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
        && year is >= 1900 and <= 9999;

    private static bool TrySplit(string? relativePath, out string[] segments)
    {
        segments = [];
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/').Trim();
        if (normalized.StartsWith('/') || normalized.Contains(':'))
        {
            return false;
        }

        segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0
            && segments.All(segment => segment is not "." and not ".." && !segment.Contains("..", StringComparison.Ordinal));
    }
}
