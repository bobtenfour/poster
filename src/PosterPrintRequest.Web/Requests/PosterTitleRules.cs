namespace PosterPrintRequest.Web.Requests;

/// <summary>TDX Integration Contract section 8.1: trimmed, 1 to 200 characters, no control characters.</summary>
public static class PosterTitleRules
{
    public const int MaxLength = 200;

    public static string? Check(string? value)
    {
        var title = value?.Trim() ?? "";
        if (title.Length == 0)
        {
            return "Enter the poster title.";
        }

        if (title.Length > MaxLength)
        {
            return $"Use {MaxLength} characters or fewer.";
        }

        return title.Any(char.IsControl) ? "Remove line breaks and tabs from the title." : null;
    }
}
