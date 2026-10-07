namespace PosterPrintRequest.Web.Requests;

public static class DraftIds
{
    public static bool IsValid(string? draftId) =>
        Guid.TryParseExact(draftId, "N", out _);

    public static string Create() => Guid.NewGuid().ToString("N");
}
