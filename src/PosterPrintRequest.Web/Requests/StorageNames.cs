using System.Globalization;
using System.Text;

namespace PosterPrintRequest.Web.Requests;

public static class StorageNames
{
    public const string Events = "EVENTS";

    public const string WithoutEvent = "WITHOUT-EVENT";

    public const string PosterPdf = "Poster.pdf";

    public const string PosterPptx = "Poster.pptx";

    public const string ApprovalSheet = "Approval-Sheet.pdf";

    public static string Year(DateTime dateIn) => dateIn.ToString("yyyy", CultureInfo.InvariantCulture);

    public static string PosterFileName(string detectedFormat) =>
        string.Equals(detectedFormat, "PPTX", StringComparison.OrdinalIgnoreCase) ? PosterPptx : PosterPdf;

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
