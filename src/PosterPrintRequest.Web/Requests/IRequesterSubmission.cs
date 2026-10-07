namespace PosterPrintRequest.Web.Requests;

public sealed class SubmissionOutcome
{
    public bool Saved { get; init; }

    public string? PosterId { get; init; }

    public string? Message { get; init; }

    public IReadOnlyDictionary<string, string> FieldErrors { get; init; } =
        new Dictionary<string, string>();

    public static SubmissionOutcome Success(string posterId) => new() { Saved = true, PosterId = posterId };

    public static SubmissionOutcome Failure(string message, IReadOnlyDictionary<string, string>? fieldErrors = null) =>
        new()
        {
            Saved = false,
            Message = message,
            FieldErrors = fieldErrors ?? new Dictionary<string, string>()
        };
}

public interface IRequesterSubmission
{
    Task<SubmissionOutcome> SubmitAsync(RequesterDraft draft, CancellationToken cancellationToken);
}
