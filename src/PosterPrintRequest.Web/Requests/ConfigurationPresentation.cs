namespace PosterPrintRequest.Web.Requests;

public static class ConfigurationPresentation
{
    public const string Active = "Active";

    public const string Inactive = "Inactive";

    public const string All = "All";

    public static IReadOnlyList<T> Apply<T>(
        IEnumerable<T> records,
        string status,
        string? search,
        Func<T, string> text,
        Func<T, bool> active)
    {
        IEnumerable<T> filtered = status switch
        {
            Inactive => records.Where(record => !active(record)),
            All => records,
            _ => records.Where(record => active(record))
        };

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            filtered = filtered.Where(record => text(record).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.ToArray();
    }
}
