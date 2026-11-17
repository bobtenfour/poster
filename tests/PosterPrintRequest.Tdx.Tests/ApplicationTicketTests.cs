using System.Net;
using System.Text;
using System.Text.Json;

namespace PosterPrintRequest.Tdx.Tests;

// TDX Integration Contract sections 6, 10.3 and 11: the generalized library used by PosterPrintRequest.
public class ApplicationTicketTests
{
    private static readonly Guid PersonUid = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task Every_request_carries_the_user_agent()
    {
        var handler = new HeaderHandler(_ => Ok("\"fake-token\""));
        using var client = Client(handler);

        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        Assert.Equal(TdxClient.UserAgent, Assert.Single(handler.UserAgents));
    }

    [Fact]
    public async Task Application_ticket_sends_its_own_content_requestor_account_and_no_requestor_creation()
    {
        var handler = new HeaderHandler(call => call.Path switch
        {
            var p when p.EndsWith("/auth", StringComparison.Ordinal) => Ok("\"fake-token\""),
            var p when p.EndsWith("/search", StringComparison.Ordinal) => Ok("[]"),
            _ => Json(HttpStatusCode.Created, "{\"ID\":77,\"Title\":\"x\"}")
        });
        using var client = Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var result = await client.CreateIfAbsentAsync(Request(PersonUid, 900), null, CancellationToken.None);

        Assert.Equal(TdxOutcome.Created, result.Outcome);
        Assert.Equal(77, result.TicketId);
        Assert.True(result.CreateSent);
        var create = Assert.Single(handler.Calls, call => call.Method == HttpMethod.Post && call.Path.EndsWith("/tickets", StringComparison.Ordinal));
        Assert.Contains("AllowRequestorCreation=false", create.Query, StringComparison.Ordinal);
        Assert.Contains("NotifyRequestor=false", create.Query, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(create.Body!);
        Assert.Equal("POSTER-2026-000123 — My poster", body.RootElement.GetProperty("Title").GetString());
        Assert.Equal("Requester: Ada Lovelace", body.RootElement.GetProperty("Description").GetString());
        Assert.Equal(900, body.RootElement.GetProperty("AccountID").GetInt32());
        Assert.Equal(PersonUid, body.RootElement.GetProperty("RequestorUid").GetGuid());
    }

    [Fact]
    public async Task Without_requestor_the_fallback_account_is_sent_and_no_requestor_field()
    {
        var handler = new HeaderHandler(call => call.Path switch
        {
            var p when p.EndsWith("/auth", StringComparison.Ordinal) => Ok("\"fake-token\""),
            var p when p.EndsWith("/search", StringComparison.Ordinal) => Ok("[]"),
            _ => Json(HttpStatusCode.Created, "{\"ID\":78}")
        });
        using var client = Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        await client.CreateIfAbsentAsync(Request(null, null), null, CancellationToken.None);

        var create = handler.Calls.Single(call => call.Path.EndsWith("/tickets", StringComparison.Ordinal));
        using var body = JsonDocument.Parse(create.Body!);
        Assert.Equal(5, body.RootElement.GetProperty("AccountID").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("RequestorUid", out _));
    }

    [Fact]
    public async Task Application_lookup_matches_only_titles_that_start_with_the_poster_id()
    {
        var handler = new HeaderHandler(call => call.Path switch
        {
            var p when p.EndsWith("/auth", StringComparison.Ordinal) => Ok("\"fake-token\""),
            _ => Ok("[{\"ID\":1,\"Title\":\"Re: POSTER-2026-000123 — My poster\"},{\"ID\":2,\"Title\":\"POSTER-2026-0001234 — other\"},{\"ID\":3,\"Title\":\"POSTER-2026-000123 — My poster\"}]")
        });
        using var client = Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var lookup = await client.LookupAsync(Request(null, null), CancellationToken.None);

        Assert.True(lookup.Succeeded);
        Assert.Equal(3, Assert.Single(lookup.MatchingTicketIds));
    }

    [Fact]
    public async Task Existing_ticket_is_not_created_again_and_before_create_does_not_run()
    {
        var ran = false;
        var handler = new HeaderHandler(call => call.Path switch
        {
            var p when p.EndsWith("/auth", StringComparison.Ordinal) => Ok("\"fake-token\""),
            _ => Ok("[{\"ID\":3,\"Title\":\"POSTER-2026-000123 — My poster\"}]")
        });
        using var client = Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var result = await client.CreateIfAbsentAsync(Request(null, null), _ => { ran = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(TdxOutcome.AlreadyExists, result.Outcome);
        Assert.False(result.CreateSent);
        Assert.False(ran);
        Assert.DoesNotContain(handler.Calls, call => call.Path.EndsWith("/tickets", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Before_create_runs_after_the_lookup_and_a_failure_there_sends_no_creation()
    {
        var order = new List<string>();
        var handler = new HeaderHandler(call =>
        {
            order.Add(call.Path);
            return call.Path.EndsWith("/auth", StringComparison.Ordinal) ? Ok("\"fake-token\"") : Ok("[]");
        });
        using var client = Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateIfAbsentAsync(
            Request(null, null),
            _ =>
            {
                order.Add("marker");
                throw new InvalidOperationException("marker not stored");
            },
            CancellationToken.None));

        Assert.EndsWith("/search", order[^2], StringComparison.Ordinal);
        Assert.Equal("marker", order[^1]);
        Assert.DoesNotContain(handler.Calls, call => call.Path.EndsWith("/tickets", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.OK, false)]
    public async Task Only_a_readable_4xx_proves_that_no_ticket_was_created(HttpStatusCode status, bool rejected)
    {
        var handler = new HeaderHandler(call => call.Path switch
        {
            var p when p.EndsWith("/auth", StringComparison.Ordinal) => Ok("\"fake-token\""),
            var p when p.EndsWith("/search", StringComparison.Ordinal) => Ok("[]"),
            _ => Json(status, "{\"Message\":\"no\"}")
        });
        using var client = Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var result = await client.CreateIfAbsentAsync(Request(null, null), null, CancellationToken.None);

        Assert.Equal(TdxOutcome.Failed, result.Outcome);
        Assert.True(result.CreateSent);
        Assert.Equal(rejected, result.CreateRejected);
    }

    [Fact]
    public async Task Requestor_is_found_by_exact_email_with_its_department()
    {
        var handler = new HeaderHandler(call => call.Path switch
        {
            var p when p.EndsWith("/auth", StringComparison.Ordinal) => Ok("\"fake-token\""),
            _ => Ok(People(("ada@example.edu.other", "Ada Other", 1), ("ADA@example.edu", "Ada Lovelace", 42)))
        });
        using var client = Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var resolution = await client.ResolveRequestorAsync("  Ada@Example.edu ", "Ada Lovelace", CancellationToken.None);

        Assert.Equal("email", resolution.MatchedBy);
        Assert.Equal(42, resolution.AccountId);
        Assert.NotNull(resolution.RequestorUid);
        var search = handler.Calls.Single(call => call.Path.EndsWith("/people/search", StringComparison.Ordinal));
        Assert.Contains("ada@example.edu", search.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Requestor_falls_back_to_exact_name_when_email_gives_no_single_person()
    {
        var handler = new HeaderHandler(call =>
        {
            if (call.Path.EndsWith("/auth", StringComparison.Ordinal))
                return Ok("\"fake-token\"");
            return call.Body!.Contains('@')
                ? Ok("[]")
                : Ok(People(("x@example.edu", "Ada  Lovelace", 7), ("y@example.edu", "Ada Lovelace King", 8)));
        });
        using var client = Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var resolution = await client.ResolveRequestorAsync("unknown@example.edu", "ada lovelace", CancellationToken.None);

        Assert.Equal("name", resolution.MatchedBy);
        Assert.Equal(7, resolution.AccountId);
    }

    [Fact]
    public async Task Several_matching_people_give_no_requestor()
    {
        var handler = new HeaderHandler(call => call.Path.EndsWith("/auth", StringComparison.Ordinal)
            ? Ok("\"fake-token\"")
            : Ok(People(("a@example.edu", "Ada Lovelace", 1), ("b@example.edu", "Ada Lovelace", 2))));
        using var client = Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var resolution = await client.ResolveRequestorAsync("not-an-email", "Ada Lovelace", CancellationToken.None);

        Assert.Null(resolution.RequestorUid);
        Assert.Null(resolution.MatchedBy);
        Assert.False(resolution.Retry);
        Assert.DoesNotContain(handler.Calls, call => call.Body is not null && call.Body.Contains("not-an-email", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task A_failed_people_search_continues_without_requestor_or_retries(HttpStatusCode status, bool retry)
    {
        var handler = new HeaderHandler(call => call.Path.EndsWith("/auth", StringComparison.Ordinal)
            ? Ok("\"fake-token\"")
            : Json(status, "{}"));
        using var client = Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var resolution = await client.ResolveRequestorAsync("ada@example.edu", "Ada Lovelace", CancellationToken.None);

        Assert.Null(resolution.RequestorUid);
        Assert.Equal(retry, resolution.Retry);
    }

    private static TicketRequest Request(Guid? requestor, int? account) => new()
    {
        PosterId = "POSTER-2026-000123",
        Title = "POSTER-2026-000123 — My poster",
        Description = "Requester: Ada Lovelace",
        RequestorUid = requestor,
        AccountId = account,
        Match = TicketMatch.TitleStartsWithPosterId,
        DisallowRequestorCreation = true
    };

    private static TdxClient Client(HttpMessageHandler handler) =>
        new(
            new Uri("https://tdx.example/tdx-test-base"),
            new TicketConfiguration { ApplicationId = 1, FormId = 1, TypeId = 1, StatusId = 1, PriorityId = 1, AccountId = 5 },
            new LookupSchedule(2, TimeSpan.Zero),
            handler,
            (_, _) => Task.CompletedTask);

    private static string People(params (string Email, string Name, int Account)[] people) =>
        JsonSerializer.Serialize(people.Select(person => new Dictionary<string, object>
        {
            ["UID"] = Guid.NewGuid().ToString(),
            ["FullName"] = person.Name,
            ["PrimaryEmail"] = person.Email,
            ["DefaultAccountID"] = person.Account
        }));

    private static HttpResponseMessage Ok(string json) => Json(HttpStatusCode.OK, json);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record Call(HttpMethod Method, string Path, string Query, string? Body);

    private sealed class HeaderHandler(Func<Call, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];

        public List<string> UserAgents { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            var call = new Call(request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, body);
            Calls.Add(call);
            return respond(call);
        }
    }
}
