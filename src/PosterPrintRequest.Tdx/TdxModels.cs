namespace PosterPrintRequest.Tdx;

public sealed class TicketAttribute
{
    public int Id { get; init; }

    public string Value { get; init; } = string.Empty;
}

public sealed class TicketConfiguration
{
    public int ApplicationId { get; init; }

    public int FormId { get; init; }

    public int TypeId { get; init; }

    public int StatusId { get; init; }

    public int PriorityId { get; init; }

    public int AccountId { get; init; }

    public int? SourceId { get; init; }

    public int? ResponsibleGroupId { get; init; }

    public IReadOnlyList<TicketAttribute> CustomAttributes { get; init; } = [];
}

public sealed class LookupSchedule
{
    public LookupSchedule(int attempts, TimeSpan interval)
    {
        if (attempts < 1)
            throw new ArgumentOutOfRangeException(nameof(attempts), "Lookup attempts must be at least 1.");
        if (interval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "The lookup interval cannot be negative.");

        Attempts = attempts;
        Interval = interval;
    }

    public int Attempts { get; }

    public TimeSpan Interval { get; }
}

public enum TdxOutcome
{
    Created,
    AlreadyExists,
    Uncertain,
    Failed
}

public enum AuthResponseForm
{
    JsonString,
    TextPlain
}

/// <summary>How a ticket returned by the lookup is recognized as the ticket of a Poster ID.</summary>
public enum TicketMatch
{
    /// <summary>Validator tickets: the title contains the exact test Poster ID.</summary>
    TitleContainsPosterId,

    /// <summary>Application tickets: the title starts with the Poster ID followed by a space.</summary>
    TitleStartsWithPosterId
}

/// <summary>The content of one ticket to create. The caller supplies every value; the library adds no data.</summary>
public sealed class TicketRequest
{
    public required string PosterId { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    /// <summary>TDX person UID of the requestor, or null to send no requestor.</summary>
    public Guid? RequestorUid { get; init; }

    /// <summary>Account (Acct/Dept) for this ticket, or null to use TicketConfiguration.AccountId.</summary>
    public int? AccountId { get; init; }

    public TicketMatch Match { get; init; } = TicketMatch.TitleContainsPosterId;

    /// <summary>When true, the creation also sends AllowRequestorCreation=false.</summary>
    public bool DisallowRequestorCreation { get; init; }

    public static TicketRequest ForValidatorTest(string posterId) => new()
    {
        PosterId = posterId,
        Title = TestTicketText.TitleFor(posterId),
        Description = TestTicketText.RequesterName,
        Match = TicketMatch.TitleContainsPosterId
    };

    public bool IsMatch(string? title)
    {
        if (title is null)
            return false;
        return Match == TicketMatch.TitleStartsWithPosterId
            ? title.StartsWith(PosterId + " ", StringComparison.Ordinal)
            : title.Contains(PosterId, StringComparison.Ordinal);
    }
}

/// <summary>A person returned by the TDX people search.</summary>
public sealed record PersonSummary(Guid Uid, string? FullName, string? PrimaryEmail, int? DefaultAccountId);

public sealed class PeopleSearchResult
{
    public bool Succeeded { get; init; }

    public int? HttpStatus { get; init; }

    public IReadOnlyList<PersonSummary> People { get; init; } = [];
}

/// <summary>Outcome of the requestor identification (TDX Integration Contract section 6).</summary>
public sealed class RequestorResolution
{
    /// <summary>True when a search failed in a way that may succeed later; the caller should retry later.</summary>
    public bool Retry { get; init; }

    public Guid? RequestorUid { get; init; }

    public int? AccountId { get; init; }

    /// <summary>"email", "name", or null when no single person matched.</summary>
    public string? MatchedBy { get; init; }
}

public static class TestTicketText
{
    public const string RequesterName = "Integration Test";

    public static string TitleFor(string posterId) =>
        $"[TEST] PosterPrintRequest integration — {posterId}";
}

public static class NotificationSuppression
{
    public const string EnableNotifyReviewer = "EnableNotifyReviewer";

    public const string NotifyRequestor = "NotifyRequestor";

    public const string NotifyResponsible = "NotifyResponsible";

    public const string Query = "EnableNotifyReviewer=false&NotifyRequestor=false&NotifyResponsible=false";

    public static IReadOnlyDictionary<string, bool> DisabledValues { get; } =
        new Dictionary<string, bool>
        {
            [EnableNotifyReviewer] = false,
            [NotifyRequestor] = false,
            [NotifyResponsible] = false
        };
}

public sealed class OperationEvidence
{
    public DateTimeOffset Timestamp { get; init; }

    public string Operation { get; init; } = string.Empty;

    public string Method { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public string? RequestPayload { get; init; }

    public int? HttpStatus { get; init; }

    public string? ResponseData { get; init; }

    public int? TicketId { get; init; }

    public string? Error { get; init; }
}

public sealed class LookupResult
{
    public bool Succeeded { get; init; }

    public IReadOnlyList<int> MatchingTicketIds { get; init; } = [];

    public int ReturnedTicketCount { get; init; }

    public OperationEvidence Evidence { get; init; } = new();

    public string? Error { get; init; }
}

public sealed class CreationResult
{
    public TdxOutcome Outcome { get; init; }

    public string PosterId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string RequesterName { get; init; } = TestTicketText.RequesterName;

    public string RequestorCase { get; init; } = "AccountID from the settings file. No RequestorUid was sent.";

    public bool RequestorUidBlocker { get; init; }

    public int? TicketId { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<OperationEvidence> Operations { get; init; } = [];

    public IReadOnlyDictionary<string, bool> NotificationParameters { get; init; } =
        NotificationSuppression.DisabledValues;

    public string CreationResponseFinding { get; init; } = "Not observed.";

    public bool? FoundByLookupAfterUncertainCreation { get; init; }

    public int? LookupAttemptsAfterUncertainCreation { get; init; }

    /// <summary>True when a creation request may have reached TDX.</summary>
    public bool CreateSent { get; internal set; }

    /// <summary>True when TDX answered the creation with a readable HTTP 4xx other than 408, which proves no ticket was created.</summary>
    public bool CreateRejected { get; internal set; }
}

public interface ITdxSession : IDisposable
{
    Task<AuthResponseForm> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);

    Task<CreationResult> CreateIfAbsentAsync(string posterId, CancellationToken cancellationToken);

    Task<LookupResult> LookupAsync(string posterId, CancellationToken cancellationToken);
}
