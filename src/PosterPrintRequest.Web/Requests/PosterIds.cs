using System.Globalization;
using System.Text.RegularExpressions;

namespace PosterPrintRequest.Web.Requests;

public static class PosterIds
{
    public const int MaximumSequence = 999_999;

    private static readonly Regex Pattern = new(
        @"^POSTER-(\d{4})-(\d{6})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsPublic(string? value) => TryParse(value, out _, out _);

    public static bool TryParse(string? value, out int year, out int sequence)
    {
        year = 0;
        sequence = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = Pattern.Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        sequence = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return sequence >= 1;
    }

    public static string Format(int year, int sequence) =>
        "POSTER-"
        + year.ToString("0000", CultureInfo.InvariantCulture)
        + "-"
        + sequence.ToString("000000", CultureInfo.InvariantCulture);

    public static int NextSequence(IEnumerable<string> existingIds, int year)
    {
        var max = 0;
        foreach (var id in existingIds)
        {
            if (TryParse(id, out var parsedYear, out var sequence) && parsedYear == year)
            {
                max = Math.Max(max, sequence);
            }
        }

        return max + 1;
    }
}
