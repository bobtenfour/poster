using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PosterPrintRequest.Tdx;

public sealed class TdxClient : ITdxSession
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Uri _base;
    private readonly TicketConfiguration _configuration;
    private readonly LookupSchedule _schedule;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private string? _token;
    private bool _disposed;

    public TdxClient(
        Uri baseAddress,
        TicketConfiguration configuration,
        LookupSchedule schedule,
        HttpMessageHandler? handler = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(schedule);
        _base = TdxAddresses.ValidateBaseAddress(baseAddress);
        _configuration = configuration;
        _schedule = schedule;
        _delay = delay ?? Task.Delay;
        var ownsHandler = handler is null;
        handler ??= new SocketsHttpHandler { AllowAutoRedirect = false };
        _http = new HttpClient(handler, disposeHandler: ownsHandler);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        if (requestTimeout is TimeSpan timeout)
            _http.Timeout = timeout;
    }

    /// <summary>Sent on every request (TDX Integration Contract section 10.3).</summary>
    public const string UserAgent = "PosterPrintRequest.Tdx/1.0";

    public async Task<AuthResponseForm> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["UserName"] = userName,
            ["Password"] = password
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, TdxAddresses.Combine(_base, "api/auth"))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var response = await SendAsync(request, "authenticate", payload: null, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var media = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (response.StatusCode != HttpStatusCode.OK)
            throw new TdxRequestRefusedException($"Authentication failed with HTTP {(int)response.StatusCode}.");

        // The form is decided by the body, not by the Content-Type header: a JSON string starts with a quote;
        // otherwise the body must be a single plain token. The header is only recorded in error messages.
        AuthResponseForm form;
        string token;
        var trimmed = body.Trim();
        if (trimmed.StartsWith('"'))
        {
            try
            {
                token = JsonSerializer.Deserialize<string>(trimmed) ?? string.Empty;
            }
            catch (JsonException)
            {
                throw new TdxRequestRefusedException($"The authentication response looked like a JSON string but could not be read (Content-Type: {DescribeMedia(media)}).");
            }

            form = AuthResponseForm.JsonString;
        }
        else if (trimmed.Length > 0 && !trimmed.Any(char.IsWhiteSpace) && trimmed.IndexOfAny(['<', '>', '{', '[']) < 0)
        {
            token = trimmed;
            form = AuthResponseForm.TextPlain;
        }
        else
        {
            throw new TdxRequestRefusedException($"The authentication response was neither a JSON string nor a plain token (Content-Type: {DescribeMedia(media)}).");
        }

        if (token.Length == 0)
            throw new TdxRequestRefusedException("The authentication response did not contain a token.");

        _token = token;
        return form;
    }

    private static string DescribeMedia(string media) => media.Length == 0 ? "none" : media;

    public Task<CreationResult> CreateIfAbsentAsync(string posterId, CancellationToken cancellationToken) =>
        CreateIfAbsentAsync(TicketRequest.ForValidatorTest(posterId), null, cancellationToken);

    /// <summary>
    /// Looks up the ticket of the request and creates it only when the lookup succeeded and found none.
    /// <paramref name="beforeCreate"/> runs after that lookup and before the creation request is sent; if it throws,
    /// no creation is sent.
    /// </summary>
    public async Task<CreationResult> CreateIfAbsentAsync(
        TicketRequest request,
        Func<CancellationToken, Task>? beforeCreate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(_token))
            throw new InvalidOperationException("Authenticate before creating a ticket.");

        var posterId = request.PosterId;
        var title = request.Title;
        var operations = new List<OperationEvidence>();
        var lookup = await LookupOnceAsync(request, cancellationToken);
        operations.Add(lookup.Evidence);
        if (!lookup.Succeeded)
        {
            return Result(
                TdxOutcome.Failed,
                posterId,
                title,
                null,
                false,
                lookup.Error ?? "The pre-creation lookup failed.",
                operations,
                "Not observed.",
                null,
                null);
        }

        if (lookup.MatchingTicketIds.Count > 1)
        {
            return Result(
                TdxOutcome.Failed,
                posterId,
                title,
                null,
                false,
                "More than one ticket title contains the Poster ID. No ticket was created.",
                operations,
                "Not observed.",
                null,
                null);
        }

        if (lookup.MatchingTicketIds.Count == 1)
        {
            return Result(
                TdxOutcome.AlreadyExists,
                posterId,
                title,
                lookup.MatchingTicketIds[0],
                false,
                null,
                operations,
                "Not observed. No creation was sent.",
                null,
                null);
        }

        if (beforeCreate is not null)
            await beforeCreate(cancellationToken);

        CreationResult sent;
        try
        {
            var created = await SendCreateAsync(request, cancellationToken);
            operations.Add(created.Evidence);
            if (created.Unreadable)
                return Sent(await RecoverAsync(request, operations, cancellationToken), false);

            if (created.TicketId is int ticketId)
            {
                operations.Add(await TryReadAsync(ticketId, cancellationToken));
                return Sent(Result(
                    TdxOutcome.Created,
                    posterId,
                    title,
                    ticketId,
                    false,
                    null,
                    operations,
                    "HTTP 201 returned Ticket.ID.",
                    null,
                    null), false);
            }

            return Sent(Result(
                TdxOutcome.Failed,
                posterId,
                title,
                null,
                created.RequestorUidBlocker,
                created.Error,
                operations,
                created.Finding,
                null,
                null), created.Status is >= 400 and < 500 and not 408);
        }
        catch (RedirectRefusedException ex)
        {
            operations.Add(ex.Evidence);
            sent = Result(
                TdxOutcome.Failed,
                posterId,
                title,
                null,
                false,
                ex.Message,
                operations,
                "A redirect response was refused and was not followed.",
                null,
                null);
            return Sent(sent, false);
        }
        catch (Exception ex) when (IsLost(ex, cancellationToken))
        {
            operations.Add(Lost("create", CreateRelativePath(), "The creation response was lost."));
            return Sent(await RecoverAsync(request, operations, cancellationToken), false);
        }
    }

    private static CreationResult Sent(CreationResult result, bool rejected)
    {
        result.CreateSent = true;
        result.CreateRejected = rejected;
        return result;
    }

    public Task<LookupResult> LookupAsync(string posterId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(_token))
            throw new InvalidOperationException("Authenticate before looking up a ticket.");

        return LookupOnceAsync(TicketRequest.ForValidatorTest(posterId), cancellationToken);
    }

    /// <summary>Looks up the ticket of the request with the request's match rule. Never creates.</summary>
    public Task<LookupResult> LookupAsync(TicketRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(_token))
            throw new InvalidOperationException("Authenticate before looking up a ticket.");

        return LookupOnceAsync(request, cancellationToken);
    }

    /// <summary>POST api/people/search with SearchText. Never creates or changes a person.</summary>
    public async Task<PeopleSearchResult> SearchPeopleAsync(string searchText, int maxResults, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(_token))
            throw new InvalidOperationException("Authenticate before searching people.");

        var text = searchText.Length > 50 ? searchText[..50] : searchText;
        var payload = JsonSerializer.Serialize(new Dictionary<string, object> { ["SearchText"] = text, ["MaxResults"] = maxResults });
        using var request = new HttpRequestMessage(HttpMethod.Post, TdxAddresses.Combine(_base, "api/people/search"))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        Authorize(request);
        try
        {
            using var response = await SendAsync(request, "people", payload: null, cancellationToken);
            var status = (int)response.StatusCode;
            if (response.StatusCode != HttpStatusCode.OK)
                return new PeopleSearchResult { Succeeded = false, HttpStatus = status };

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                return new PeopleSearchResult { Succeeded = true, HttpStatus = status, People = ReadPeople(body) };
            }
            catch (JsonException)
            {
                return new PeopleSearchResult { Succeeded = false, HttpStatus = status };
            }
        }
        catch (RedirectRefusedException ex)
        {
            return new PeopleSearchResult { Succeeded = false, HttpStatus = ex.Evidence.HttpStatus };
        }
        catch (Exception ex) when (IsLost(ex, cancellationToken))
        {
            return new PeopleSearchResult { Succeeded = false };
        }
    }

    /// <summary>
    /// TDX Integration Contract section 6: by email (exact PrimaryEmail), then by name (exact FullName); a person is used
    /// only when exactly one matches. Comparisons are case-insensitive with repeated spaces collapsed.
    /// </summary>
    public async Task<RequestorResolution> ResolveRequestorAsync(string? email, string? name, CancellationToken cancellationToken)
    {
        var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedEmail.Contains('@'))
        {
            var byEmail = await SearchPeopleAsync(normalizedEmail, 25, cancellationToken);
            if (!byEmail.Succeeded)
                return Unresolved(byEmail);
            var matches = byEmail.People
                .Where(person => string.Equals(person.PrimaryEmail?.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 1)
                return new RequestorResolution { RequestorUid = matches[0].Uid, AccountId = matches[0].DefaultAccountId, MatchedBy = "email" };
        }

        var normalizedName = CollapseSpaces(name);
        if (normalizedName.Length > 0)
        {
            var byName = await SearchPeopleAsync(normalizedName, 25, cancellationToken);
            if (!byName.Succeeded)
                return Unresolved(byName);
            var matches = byName.People
                .Where(person => string.Equals(CollapseSpaces(person.FullName), normalizedName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 1)
                return new RequestorResolution { RequestorUid = matches[0].Uid, AccountId = matches[0].DefaultAccountId, MatchedBy = "name" };
        }

        return new RequestorResolution();
    }

    // 401/403 do not change by retrying: continue without a requestor. Anything else may be transient: retry later.
    private static RequestorResolution Unresolved(PeopleSearchResult result) =>
        result.HttpStatus is 401 or 403 ? new RequestorResolution() : new RequestorResolution { Retry = true };

    private static string CollapseSpaces(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static List<PersonSummary> ReadPeople(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("The people search response was not an array.");

        var people = new List<PersonSummary>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            if (!item.TryGetProperty("UID", out var uidElement) || uidElement.ValueKind != JsonValueKind.String
                || !Guid.TryParse(uidElement.GetString(), out var uid))
                continue;
            int? account = item.TryGetProperty("DefaultAccountID", out var accountElement) && accountElement.TryGetInt32(out var a) && a > 0 ? a : null;
            people.Add(new PersonSummary(uid, ReadString(item, "FullName"), ReadString(item, "PrimaryEmail"), account));
        }

        return people;
    }

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    public void Dispose()
    {
        _token = null;
        _http.Dispose();
        _disposed = true;
    }

    private async Task<CreationResult> RecoverAsync(
        TicketRequest request,
        List<OperationEvidence> operations,
        CancellationToken cancellationToken)
    {
        var posterId = request.PosterId;
        var title = request.Title;
        for (var attempt = 1; attempt <= _schedule.Attempts; attempt++)
        {
            if (attempt > 1)
                await _delay(_schedule.Interval, cancellationToken);

            var lookup = await LookupOnceAsync(request, cancellationToken);
            operations.Add(lookup.Evidence);
            if (lookup.Succeeded && lookup.MatchingTicketIds.Count == 1)
            {
                var ticketId = lookup.MatchingTicketIds[0];
                operations.Add(await TryReadAsync(ticketId, cancellationToken));
                return Result(
                    TdxOutcome.Created,
                    posterId,
                    title,
                    ticketId,
                    false,
                    null,
                    operations,
                    "The creation response was lost or unreadable. Ticket.ID was taken from the lookup.",
                    true,
                    attempt);
            }

            if (lookup.Succeeded && lookup.MatchingTicketIds.Count > 1)
            {
                return Result(
                    TdxOutcome.Uncertain,
                    posterId,
                    title,
                    null,
                    false,
                    "More than one ticket title contains the Poster ID after an uncertain creation. Check the sandbox manually. No further creation was attempted.",
                    operations,
                    "The creation response was lost or unreadable.",
                    null,
                    attempt);
            }
        }

        return Result(
            TdxOutcome.Uncertain,
            posterId,
            title,
            null,
            false,
            "The ticket was not found before the lookup schedule ended. Check the sandbox manually. No further creation was attempted.",
            operations,
            "The creation response was lost or unreadable. Ticket.ID was not observed.",
            false,
            _schedule.Attempts);
    }

    private async Task<CreateCall> SendCreateAsync(TicketRequest ticket, CancellationToken cancellationToken)
    {
        var payload = SerializeCreate(ticket);
        var query = ticket.DisallowRequestorCreation
            ? NotificationSuppression.Query + "&AllowRequestorCreation=false"
            : NotificationSuppression.Query;
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            TdxAddresses.Combine(_base, CreateRelativePath(), query))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        Authorize(request);

        using var response = await SendAsync(request, "create", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var status = (int)response.StatusCode;
        var evidence = Evidence("create", "POST", Relative(request.RequestUri!), payload, status, Scrub(body), null, null);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            if (TryReadTicket(body, out var ticketId, out _))
            {
                evidence = Evidence("create", "POST", Relative(request.RequestUri!), payload, status, $"Ticket.ID {ticketId}.", ticketId, null);
                return CreateCall.Created(evidence, ticketId);
            }

            evidence = Evidence("create", "POST", Relative(request.RequestUri!), payload, status, "The creation response did not contain Ticket.ID.", null, "The creation response was unreadable.");
            return CreateCall.Uncertain(evidence, "The creation response was unreadable.");
        }

        if (status == 408 || status >= 500)
        {
            var uncertainError = $"Creation returned HTTP {status}. The response does not prove that no ticket exists.";
            evidence = Evidence("create", "POST", Relative(request.RequestUri!), payload, status, Scrub(Truncate(body)), null, uncertainError);
            return CreateCall.Uncertain(evidence, $"Creation returned HTTP {status}. The response does not prove that no ticket exists.");
        }

        var blocker = status >= 400 && status < 500 && body.Contains("RequestorUid", StringComparison.OrdinalIgnoreCase);
        var error = blocker
            ? "The sandbox rejected creation because RequestorUid was missing. This is a blocker. No workaround was attempted."
            : $"Creation failed with HTTP {status}.";
        evidence = Evidence("create", "POST", Relative(request.RequestUri!), payload, status, Scrub(Truncate(body)), null, error);
        return CreateCall.Rejected(evidence, error, blocker, $"HTTP {status}. Ticket.ID was not returned.", status);
    }

    private async Task<LookupResult> LookupOnceAsync(TicketRequest ticket, CancellationToken cancellationToken)
    {
        var posterId = ticket.PosterId;
        var relative = $"api/{_configuration.ApplicationId}/tickets/search";
        var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["SearchText"] = posterId });
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, TdxAddresses.Combine(_base, relative))
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            Authorize(request);
            using var response = await SendAsync(request, "lookup", payload, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = (int)response.StatusCode;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var error = $"Lookup failed with HTTP {status}.";
                return new LookupResult
                {
                    Succeeded = false,
                    Error = error,
                    Evidence = Evidence("lookup", "POST", relative, payload, status, Scrub(Truncate(body)), null, error)
                };
            }

            List<TicketItem> tickets;
            try
            {
                tickets = ReadTickets(body);
            }
            catch (JsonException)
            {
                const string unreadable = "The lookup response was unreadable.";
                return new LookupResult
                {
                    Succeeded = false,
                    Error = unreadable,
                    Evidence = Evidence("lookup", "POST", relative, payload, status, null, null, unreadable)
                };
            }

            var matches = tickets
                .Where(item => item.Id > 0 && ticket.IsMatch(item.Title))
                .Select(ticket => ticket.Id)
                .Distinct()
                .ToList();
            return new LookupResult
            {
                Succeeded = true,
                MatchingTicketIds = matches,
                ReturnedTicketCount = tickets.Count,
                Evidence = Evidence(
                    "lookup",
                    "POST",
                    relative,
                    payload,
                    status,
                    $"Returned {tickets.Count}. Title matches {matches.Count}.",
                    matches.Count == 1 ? matches[0] : null,
                    null)
            };
        }
        catch (RedirectRefusedException ex)
        {
            return new LookupResult
            {
                Succeeded = false,
                Error = ex.Message,
                Evidence = ex.Evidence
            };
        }
        catch (Exception ex) when (IsLost(ex, cancellationToken))
        {
            const string lost = "The lookup response was lost.";
            return new LookupResult
            {
                Succeeded = false,
                Error = lost,
                Evidence = Lost("lookup", relative, lost)
            };
        }
    }

    private async Task<OperationEvidence> TryReadAsync(int ticketId, CancellationToken cancellationToken)
    {
        var relative = $"api/{_configuration.ApplicationId}/tickets/{ticketId}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, TdxAddresses.Combine(_base, relative));
            Authorize(request);
            using var response = await SendAsync(request, "read", payload: null, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = (int)response.StatusCode;
            int? observed = null;
            if (response.StatusCode == HttpStatusCode.OK && TryReadTicket(body, out var readId, out _))
                observed = readId;

            var error = response.StatusCode == HttpStatusCode.OK ? null : $"The optional ticket read failed with HTTP {status}.";
            return Evidence("read", "GET", relative, null, status, observed is null ? Scrub(Truncate(body)) : $"Ticket.ID {observed}.", observed, error);
        }
        catch (Exception ex) when (IsLost(ex, cancellationToken) || ex is TdxRequestRefusedException)
        {
            return Lost("read", relative, "The optional ticket read failed.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        string operation,
        string? payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.RequestUri);
        TdxAddresses.EnsureRequestAllowed(_base, request.RequestUri);
        var response = await _http.SendAsync(request, cancellationToken);
        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400)
        {
            response.Dispose();
            throw new RedirectRefusedException(Evidence(operation, request.Method.Method, Relative(request.RequestUri), payload, status, null, null, "A redirect response was refused and was not followed."));
        }

        return response;
    }

    private void Authorize(HttpRequestMessage request)
    {
        if (string.IsNullOrEmpty(_token))
            throw new InvalidOperationException("Authenticate before calling TDX.");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    private string SerializeCreate(TicketRequest ticket)
    {
        var payload = new Dictionary<string, object?>
        {
            ["AccountID"] = ticket.AccountId ?? _configuration.AccountId,
            ["FormID"] = _configuration.FormId,
            ["TypeID"] = _configuration.TypeId,
            ["StatusID"] = _configuration.StatusId,
            ["PriorityID"] = _configuration.PriorityId,
            ["Title"] = ticket.Title,
            ["Description"] = ticket.Description
        };
        if (ticket.RequestorUid is Guid requestorUid)
            payload["RequestorUid"] = requestorUid;
        if (_configuration.SourceId is int sourceId)
            payload["SourceID"] = sourceId;
        if (_configuration.ResponsibleGroupId is int responsibleGroupId)
            payload["ResponsibleGroupID"] = responsibleGroupId;
        if (_configuration.CustomAttributes.Count > 0)
        {
            payload["Attributes"] = _configuration.CustomAttributes
                .Select(attribute => new Dictionary<string, object>
                {
                    ["ID"] = attribute.Id,
                    ["Value"] = attribute.Value
                })
                .ToList();
        }

        return JsonSerializer.Serialize(payload, Json);
    }

    private static bool TryReadTicket(string json, out int id, out string? title)
    {
        id = 0;
        title = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            if (!document.RootElement.TryGetProperty("ID", out var idElement) || !idElement.TryGetInt32(out id) || id <= 0)
                return false;
            if (document.RootElement.TryGetProperty("Title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
                title = titleElement.GetString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static List<TicketItem> ReadTickets(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("The lookup response was not an array.");

        var tickets = new List<TicketItem>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = 0;
            string? itemTitle = null;
            if (item.ValueKind == JsonValueKind.Object)
            {
                if (item.TryGetProperty("ID", out var idElement))
                    idElement.TryGetInt32(out id);
                if (item.TryGetProperty("Title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
                    itemTitle = titleElement.GetString();
            }

            tickets.Add(new TicketItem(id, itemTitle));
        }

        return tickets;
    }

    private string CreateRelativePath() => $"api/{_configuration.ApplicationId}/tickets";

    private string Relative(Uri uri)
    {
        var basePath = _base.AbsolutePath.TrimEnd('/');
        var path = uri.AbsolutePath;
        var relative = path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
            ? path[basePath.Length..].TrimStart('/')
            : path.TrimStart('/');
        return string.IsNullOrEmpty(uri.Query) ? relative : relative + uri.Query;
    }

    private string? Scrub(string? value)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(_token))
            return value;

        return value.Replace(_token, "[redacted]", StringComparison.Ordinal);
    }

    private static string Truncate(string value) =>
        value.Length <= 2000 ? value : value[..2000];

    private static bool IsLost(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or IOException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private OperationEvidence Lost(string operation, string relativePath, string error) =>
        Evidence(operation, operation == "read" ? "GET" : "POST", relativePath, null, null, null, null, error);

    private static OperationEvidence Evidence(
        string operation,
        string method,
        string relativePath,
        string? payload,
        int? status,
        string? response,
        int? ticketId,
        string? error) =>
        new()
        {
            Timestamp = DateTimeOffset.UtcNow,
            Operation = operation,
            Method = method,
            RelativePath = relativePath,
            RequestPayload = payload,
            HttpStatus = status,
            ResponseData = response,
            TicketId = ticketId,
            Error = error
        };

    private static CreationResult Result(
        TdxOutcome outcome,
        string posterId,
        string title,
        int? ticketId,
        bool blocker,
        string? error,
        List<OperationEvidence> operations,
        string creationFinding,
        bool? foundAfterUncertain,
        int? attempts) =>
        new()
        {
            Outcome = outcome,
            PosterId = posterId,
            Title = title,
            RequestorUidBlocker = blocker,
            TicketId = ticketId,
            Error = error,
            Operations = operations,
            CreationResponseFinding = creationFinding,
            FoundByLookupAfterUncertainCreation = foundAfterUncertain,
            LookupAttemptsAfterUncertainCreation = attempts
        };

    private sealed class RedirectRefusedException : TdxRequestRefusedException
    {
        public RedirectRefusedException(OperationEvidence evidence)
            : base(evidence.Error ?? "A redirect response was refused and was not followed.")
        {
            Evidence = evidence;
        }

        public OperationEvidence Evidence { get; }
    }

    private sealed class CreateCall
    {
        public OperationEvidence Evidence { get; init; } = new();

        public int? TicketId { get; init; }

        public bool Unreadable { get; init; }

        public bool RequestorUidBlocker { get; init; }

        public string? Error { get; init; }

        public string Finding { get; init; } = "Not observed.";

        public static CreateCall Created(OperationEvidence evidence, int ticketId) =>
            new() { Evidence = evidence, TicketId = ticketId, Finding = "HTTP 201 returned Ticket.ID." };

        public static CreateCall Uncertain(OperationEvidence evidence, string finding) =>
            new() { Evidence = evidence, Unreadable = true, Finding = finding };

        public int? Status { get; init; }

        public static CreateCall Rejected(OperationEvidence evidence, string error, bool blocker, string finding, int status) =>
            new() { Evidence = evidence, Error = error, RequestorUidBlocker = blocker, Finding = finding, Status = status };
    }

    private readonly record struct TicketItem(int Id, string? Title);
}
