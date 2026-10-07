namespace PosterPrintRequest.Web.Requests;

public interface IPosterPreflight
{
    PosterPreflightResult Inspect(Stream content);

    Task<PosterPreflightResult?> InspectStoredPosterAsync(string draftId, CancellationToken cancellationToken);
}
