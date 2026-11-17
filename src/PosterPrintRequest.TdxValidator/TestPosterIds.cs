namespace PosterPrintRequest.TdxValidator;

public static class TestPosterIds
{
    public static string Create(DateTimeOffset timestamp, Guid guid)
    {
        var utc = timestamp.ToUniversalTime();
        return $"POSTER-TEST-{utc:yyyyMMdd-HHmmss}-{guid:N}";
    }
}
