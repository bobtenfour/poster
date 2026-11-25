using System.Globalization;
using System.Text;

namespace PosterPrintRequest.Web.Requests;

public static class StorageNames
{
    public const string Events = "EVENTS";

    public const string WithoutEvent = "WITHOUT-EVENT";

    /// <summary>Name used before the 25 November 2026 amendment; recognized only so the startup rename can find it.</summary>
    public const string PosterPdf = "Poster.pdf";

    /// <summary>Name used before the 25 November 2026 amendment; recognized only so the startup rename can find it.</summary>
    public const string PosterPptx = "Poster.pptx";

    /// <summary>Name used before the 25 November 2026 amendment; recognized only so the startup rename can find it.</summary>
    public const string ApprovalSheet = "Approval-Sheet.pdf";

    public const string ApprovalSheetSuffix = " - Approval Sheet.pdf";

    public static string Year(DateTime dateIn) => dateIn.ToString("yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// Shared Storage Contract amendment of 25 November 2026: the poster file is named
    /// "&lt;Poster ID&gt; - &lt;requester name&gt; - &lt;Date In yyyy-MM-dd&gt;.pdf" (or .pptx).
    /// Null when the requester name or the Poster ID is not usable in a file name.
    /// </summary>
    public static string? PosterFileName(string posterId, string? requesterName, DateTime dateIn, string detectedFormat)
    {
        var stem = FileStem(posterId, requesterName, dateIn);
        if (stem is null)
        {
            return null;
        }

        return stem + (string.Equals(detectedFormat, "PPTX", StringComparison.OrdinalIgnoreCase) ? ".pptx" : ".pdf");
    }

    /// <summary>Shared Storage Contract amendment of 25 November 2026: "&lt;stem&gt; - Approval Sheet.pdf".</summary>
    public static string? ApprovalSheetFileName(string posterId, string? requesterName, DateTime dateIn)
    {
        var stem = FileStem(posterId, requesterName, dateIn);
        return stem is null ? null : stem + ApprovalSheetSuffix;
    }

    /// <summary>A poster file under the current rule or the earlier one.</summary>
    public static bool IsPosterFile(string? name)
    {
        if (name is PosterPdf or PosterPptx)
        {
            return true;
        }

        if (string.IsNullOrEmpty(name) || name.EndsWith(ApprovalSheetSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        var stem = name.EndsWith(".pdf", StringComparison.Ordinal) ? name[..^4]
            : name.EndsWith(".pptx", StringComparison.Ordinal) ? name[..^5]
            : null;
        return stem is not null && IsStem(stem);
    }

    /// <summary>An Approval Sheet file under the current rule or the earlier one.</summary>
    public static bool IsApprovalSheetFile(string? name)
    {
        if (name == ApprovalSheet)
        {
            return true;
        }

        return name is not null
            && name.EndsWith(ApprovalSheetSuffix, StringComparison.Ordinal)
            && IsStem(name[..^ApprovalSheetSuffix.Length]);
    }

    private static string? FileStem(string posterId, string? requesterName, DateTime dateIn)
    {
        var name = Segment(requesterName);
        if (name is null || !PosterIds.IsPublic(posterId))
        {
            return null;
        }

        return posterId + " - " + name + " - " + dateIn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static bool IsStem(string stem)
    {
        // <Poster ID> - <name> - <yyyy-MM-dd>
        const string marker = " - ";
        var first = stem.IndexOf(marker, StringComparison.Ordinal);
        var last = stem.LastIndexOf(marker, StringComparison.Ordinal);
        if (first <= 0 || last <= first)
        {
            return false;
        }

        var name = stem[(first + marker.Length)..last];
        return PosterIds.IsPublic(stem[..first])
            && DateTime.TryParseExact(stem[(last + marker.Length)..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            && Segment(name) is { } safe
            && string.Equals(safe, name, StringComparison.Ordinal);
    }

    public static string? EventFolder(string? eventName, DateTime dateIn)
    {
        var name = Segment(eventName);
        return name is null ? null : name + " " + Year(dateIn);
    }

    public static string? PosterFolder(string? requesterName, string posterId)
    {
        var name = Segment(requesterName);
        if (name is null || !PosterIds.IsPublic(posterId))
        {
            return null;
        }

        return name + " - " + posterId;
    }

    public static string? PosterIdFromFolder(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        const string marker = " - ";
        var index = folderName.LastIndexOf(marker, StringComparison.Ordinal);
        if (index <= 0)
        {
            return null;
        }

        var posterId = folderName[(index + marker.Length)..];
        if (!PosterIds.IsPublic(posterId))
        {
            return null;
        }

        var student = folderName[..index];
        var safe = Segment(student);
        return safe is not null && string.Equals(safe, student, StringComparison.Ordinal) ? posterId : null;
    }

    public static string? Segment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            builder.Append(invalid.Contains(character) || character is '/' or '\\' ? ' ' : character);
        }

        var segment = builder.ToString().Trim();
        while (segment.Contains("  ", StringComparison.Ordinal))
        {
            segment = segment.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (segment.Length > 120)
        {
            segment = segment[..120].Trim();
        }

        if (segment.Length == 0 || segment is "." or ".." || segment.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        return segment;
    }
}
