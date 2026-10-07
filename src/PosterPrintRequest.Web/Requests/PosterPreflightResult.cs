namespace PosterPrintRequest.Web.Requests;

public sealed class PosterPreflightResult
{
    public bool Passed { get; init; }

    public string Summary { get; init; } = "";

    public string? DetectedFormat { get; init; }

    public int? PageCount { get; init; }

    public decimal? WidthInches { get; init; }

    public decimal? LengthInches { get; init; }

    public IReadOnlyList<PosterPreflightCheck> Checks { get; init; } = [];
}

public sealed class PosterPreflightCheck
{
    public string Label { get; init; } = "";

    public bool Passed { get; init; }

    public string Detail { get; init; } = "";
}
