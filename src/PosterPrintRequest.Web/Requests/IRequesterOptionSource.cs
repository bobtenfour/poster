namespace PosterPrintRequest.Web.Requests;

public interface IRequesterOptionSource
{
    Task<RequesterChoices> LoadAsync(CancellationToken cancellationToken);
}
