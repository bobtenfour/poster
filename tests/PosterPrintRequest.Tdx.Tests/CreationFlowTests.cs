using System.Net;
using System.Text.Json;

using PosterPrintRequest.Tdx;

namespace PosterPrintRequest.Tdx.Tests;

public class EndpointGuardTests
{
    private static readonly Uri Base = new("https://tdx.example/tdx-test-base");

    [Fact]
    public void Construction_rejects_a_base_address_before_http()
    {
        using var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("HTTP was called."));
        var samples = new[]
        {
            "http://tdx.example/tdx-test-base",
            "https://tdx.example:8443/tdx-test-base",
            "https://user:secret@tdx.example/tdx-test-base",
            "https://tdx.example/tdx-test-base?x=1",
            "https://tdx.example/tdx-test-base#fragment",
            "https://tdx.example/tdx-test-base/../outside",
            "https://tdx.example/tdx-test-base/%2e%2e/outside",
            "https://tdx.example/%2ftdx-test-base"
        };

        foreach (var sample in samples)
        {
            Assert.ThrowsAny<Exception>(() => new TdxClient(new Uri(sample), Sample.Configuration(), Sample.Schedule(), handler));
        }

        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData("http://tdx.example/tdx-test-base/api/auth")]
    [InlineData("https://other.example/tdx-test-base/api/auth")]
    [InlineData("https://tdx.example:8443/tdx-test-base/api/auth")]
    [InlineData("https://tdx.example/tdx-test-base-other")]
    [InlineData("https://tdx.example/tdx-test-base/../outside")]
    [InlineData("https://tdx.example/tdx-test-base/%2e%2e/outside")]
    [InlineData("https://tdx.example/tdx-test-base/%2fother")]
    public void Request_guard_refuses_destinations_outside_the_base(string request)
    {
        Assert.Throws<TdxRequestRefusedException>(() => TdxAddresses.EnsureRequestAllowed(Base, new Uri(request)));
    }

    [Fact]
    public async Task Redirect_response_is_not_followed()
    {
        using var handler = new RecordingHandler((_, _) =>
        {
            var response = RecordingHandler.Plain(HttpStatusCode.Redirect, string.Empty);
            response.Headers.Location = new Uri("https://other.example/away");
            return response;
        });
        using var client = Sample.Client(handler);

        await Assert.ThrowsAnyAsync<TdxRequestRefusedException>(() => client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None));

        Assert.Single(handler.Calls);
        Assert.DoesNotContain(handler.Calls, call => call.Uri.Host == "other.example");
    }
}

public class CreationFlowTests
{
    [Fact]
    public async Task Authentication_sends_login_parameters_and_accepts_either_token_form()
    {
        await AssertAuthForm(RecordingHandler.Json(HttpStatusCode.OK, "\"json-token\""), AuthResponseForm.JsonString, "json-token");
        await AssertAuthForm(RecordingHandler.Plain(HttpStatusCode.OK, "plain-token"), AuthResponseForm.TextPlain, "plain-token");
        // A plain token labelled application/json is accepted as a plain token.
        await AssertAuthForm(RecordingHandler.Json(HttpStatusCode.OK, "header.payload.signature"), AuthResponseForm.TextPlain, "header.payload.signature");
    }

    [Fact]
    public async Task Search_hit_without_the_poster_id_in_the_title_is_not_a_match()
    {
        var posterId = Sample.PosterId;
        using var handler = Sample.Handler(call =>
        {
            if (call.Uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal))
                return RecordingHandler.Json(HttpStatusCode.OK, "[{\"ID\":7,\"Title\":\"unrelated text\"}]");
            if (call.Method == HttpMethod.Get)
                return RecordingHandler.Json(HttpStatusCode.OK, "{\"ID\":8}");
            return RecordingHandler.Json(HttpStatusCode.Created, "{\"ID\":8,\"Title\":\"created\"}");
        });
        using var client = Sample.Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var result = await client.CreateIfAbsentAsync(posterId, CancellationToken.None);

        Assert.Equal(TdxOutcome.Created, result.Outcome);
        Assert.Equal(8, result.TicketId);
        var create = Assert.Single(handler.Calls, call => Sample.IsCreate(call));
        using var body = JsonDocument.Parse(create.Body!);
        Assert.Equal(1, body.RootElement.GetProperty("AccountID").GetInt32());
        Assert.Equal(TestTicketText.RequesterName, body.RootElement.GetProperty("Description").GetString());
        Assert.Equal(TestTicketText.TitleFor(posterId), body.RootElement.GetProperty("Title").GetString());
        Assert.False(body.RootElement.TryGetProperty("RequestorUid", out _));
        Assert.False(body.RootElement.TryGetProperty("RequestorEmail", out _));
        Assert.False(body.RootElement.TryGetProperty("RequestorName", out _));
        Assert.Contains("EnableNotifyReviewer=false", create.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("NotifyRequestor=false", create.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("NotifyResponsible=false", create.Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Title_containing_the_exact_poster_id_blocks_creation()
    {
        var posterId = Sample.PosterId;
        using var handler = Sample.Handler(call =>
            RecordingHandler.Json(HttpStatusCode.OK, "[{" + "\"ID\":7,\"Title\":" + JsonSerializer.Serialize("prefix " + posterId + " suffix") + "}]"));
        using var client = Sample.Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var result = await client.CreateIfAbsentAsync(posterId, CancellationToken.None);

        Assert.Equal(TdxOutcome.AlreadyExists, result.Outcome);
        Assert.Equal(7, result.TicketId);
        Assert.DoesNotContain(handler.Calls, Sample.IsCreate);
    }

    [Fact]
    public async Task Lost_creation_response_uses_bounded_lookup_and_does_not_create_again()
    {
        var posterId = Sample.PosterId;
        var searches = 0;
        using var handler = Sample.Handler(call =>
        {
            if (!call.Uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal))
            {
                if (call.Method == HttpMethod.Get)
                    return RecordingHandler.Json(HttpStatusCode.OK, "{\"ID\":42}");
                throw new HttpRequestException("lost");
            }

            searches++;
            return searches == 1
                ? RecordingHandler.Json(HttpStatusCode.OK, "[]")
                : RecordingHandler.Json(HttpStatusCode.OK, Sample.TicketArray(42, TestTicketText.TitleFor(posterId)));
        });
        var delays = 0;
        using var client = Sample.Client(handler, attempts: 3, delay: (_, _) =>
        {
            delays++;
            return Task.CompletedTask;
        });
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var result = await client.CreateIfAbsentAsync(posterId, CancellationToken.None);

        Assert.Equal(TdxOutcome.Created, result.Outcome);
        Assert.Equal(42, result.TicketId);
        Assert.Equal(1, result.LookupAttemptsAfterUncertainCreation);
        Assert.Equal(1, handler.Calls.Count(Sample.IsCreate));
        Assert.Equal(0, delays);
    }

    [Fact]
    public async Task Lookup_that_finds_nothing_within_the_schedule_is_uncertain_and_does_not_create_again()
    {
        var delays = new List<TimeSpan>();
        using var handler = Sample.Handler(call =>
        {
            if (call.Uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal))
                return RecordingHandler.Json(HttpStatusCode.OK, "[]");
            if (call.Method == HttpMethod.Get)
                return RecordingHandler.Json(HttpStatusCode.OK, "{\"ID\":1}");
            throw new HttpRequestException("lost");
        });
        using var client = Sample.Client(handler, attempts: 3, interval: TimeSpan.FromSeconds(5), delay: (span, _) =>
        {
            delays.Add(span);
            return Task.CompletedTask;
        });
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var result = await client.CreateIfAbsentAsync(Sample.PosterId, CancellationToken.None);

        Assert.Equal(TdxOutcome.Uncertain, result.Outcome);
        Assert.Null(result.TicketId);
        Assert.Equal(1, handler.Calls.Count(Sample.IsCreate));
        Assert.Equal(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5) }, delays);
        Assert.Contains("Check the sandbox manually", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_pre_creation_lookup_prevents_creation()
    {
        using var handler = Sample.Handler(_ => RecordingHandler.Json(HttpStatusCode.Forbidden, "denied"));
        using var client = Sample.Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var result = await client.CreateIfAbsentAsync(Sample.PosterId, CancellationToken.None);

        Assert.Equal(TdxOutcome.Failed, result.Outcome);
        Assert.DoesNotContain(handler.Calls, Sample.IsCreate);
    }

    [Fact]
    public async Task Too_many_requests_on_pre_lookup_blocks_creation_and_is_not_retried()
    {
        using var handler = Sample.Handler(_ => RecordingHandler.Plain(HttpStatusCode.TooManyRequests, string.Empty));
        var delays = 0;
        using var client = Sample.Client(handler, delay: (_, _) =>
        {
            delays++;
            return Task.CompletedTask;
        });
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var result = await client.CreateIfAbsentAsync(Sample.PosterId, CancellationToken.None);

        Assert.Equal(TdxOutcome.Failed, result.Outcome);
        Assert.Equal(1, handler.Calls.Count(call => call.Uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal)));
        Assert.DoesNotContain(handler.Calls, Sample.IsCreate);
        Assert.Equal(0, delays);
    }

    [Fact]
    public async Task Too_many_requests_after_an_uncertain_creation_stays_uncertain()
    {
        var searches = 0;
        using var handler = Sample.Handler(call =>
        {
            if (call.Uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal))
            {
                searches++;
                return searches == 1
                    ? RecordingHandler.Json(HttpStatusCode.OK, "[]")
                    : RecordingHandler.Plain(HttpStatusCode.TooManyRequests, string.Empty);
            }

            throw new HttpRequestException("lost");
        });
        using var client = Sample.Client(handler, attempts: 2);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var result = await client.CreateIfAbsentAsync(Sample.PosterId, CancellationToken.None);

        Assert.Equal(TdxOutcome.Uncertain, result.Outcome);
        Assert.Equal(1, handler.Calls.Count(Sample.IsCreate));
        Assert.Equal(3, handler.Calls.Count(call => call.Uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Missing_requestor_uid_is_a_blocker_without_another_creation()
    {
        using var handler = Sample.Handler(call =>
            call.Uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal)
                ? RecordingHandler.Json(HttpStatusCode.OK, "[]")
                : RecordingHandler.Json(HttpStatusCode.BadRequest, "RequestorUid is required"));
        using var client = Sample.Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var result = await client.CreateIfAbsentAsync(Sample.PosterId, CancellationToken.None);

        Assert.Equal(TdxOutcome.Failed, result.Outcome);
        Assert.True(result.RequestorUidBlocker);
        Assert.Equal(1, handler.Calls.Count(Sample.IsCreate));
        Assert.Single(handler.Calls, call => call.Uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal));
        Assert.Contains("blocker", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repeating_the_same_poster_id_does_not_create_a_second_ticket()
    {
        var posterId = Sample.PosterId;
        var searches = 0;
        using var handler = Sample.Handler(call =>
        {
            if (call.Method == HttpMethod.Get)
                return RecordingHandler.Json(HttpStatusCode.OK, "{\"ID\":42}");
            if (call.Uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal))
            {
                searches++;
                return searches == 1
                    ? RecordingHandler.Json(HttpStatusCode.OK, "[]")
                    : RecordingHandler.Json(HttpStatusCode.OK, Sample.TicketArray(42, TestTicketText.TitleFor(posterId)));
            }

            return RecordingHandler.Json(HttpStatusCode.Created, "{\"ID\":42}");
        });
        using var client = Sample.Client(handler);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var first = await client.CreateIfAbsentAsync(posterId, CancellationToken.None);
        var second = await client.CreateIfAbsentAsync(posterId, CancellationToken.None);

        Assert.Equal(TdxOutcome.Created, first.Outcome);
        Assert.Equal(TdxOutcome.AlreadyExists, second.Outcome);
        Assert.Equal(1, handler.Calls.Count(Sample.IsCreate));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Creation_5xx_or_408_uses_bounded_lookup_and_does_not_create_again(HttpStatusCode status)
    {
        var posterId = Sample.PosterId;
        var searches = 0;
        using var handler = Sample.Handler(call =>
        {
            if (call.Method == HttpMethod.Get)
                return RecordingHandler.Json(HttpStatusCode.OK, "{\"ID\":42}");
            if (call.Uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal))
            {
                searches++;
                return searches == 1
                    ? RecordingHandler.Json(HttpStatusCode.OK, "[]")
                    : RecordingHandler.Json(HttpStatusCode.OK, Sample.TicketArray(42, TestTicketText.TitleFor(posterId)));
            }

            return RecordingHandler.Plain(status, "temporary failure");
        });
        using var client = Sample.Client(handler, attempts: 3);
        await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);

        var result = await client.CreateIfAbsentAsync(posterId, CancellationToken.None);

        Assert.Equal(TdxOutcome.Created, result.Outcome);
        Assert.Equal(42, result.TicketId);
        Assert.Equal(1, handler.Calls.Count(Sample.IsCreate));
        Assert.Contains(result.Operations, operation => operation.HttpStatus == (int)status);
    }

    private static async Task AssertAuthForm(HttpResponseMessage authResponse, AuthResponseForm expected, string token)
    {
        using var handler = new RecordingHandler((call, _) =>
        {
            if (call.Uri.AbsolutePath.EndsWith("/auth", StringComparison.Ordinal))
                return authResponse;
            return RecordingHandler.Json(HttpStatusCode.OK, Sample.TicketArray(9, TestTicketText.TitleFor(Sample.PosterId)));
        });
        using var client = Sample.Client(handler);

        var form = await client.AuthenticateAsync("fake-user", "fake-password", CancellationToken.None);
        var result = await client.CreateIfAbsentAsync(Sample.PosterId, CancellationToken.None);
        var serialized = JsonSerializer.Serialize(result);

        Assert.Equal(expected, form);
        Assert.Equal(TdxOutcome.AlreadyExists, result.Outcome);
        using var auth = JsonDocument.Parse(handler.Calls[0].Body!);
        Assert.Equal("fake-user", auth.RootElement.GetProperty("UserName").GetString());
        Assert.Equal("fake-password", auth.RootElement.GetProperty("Password").GetString());
        Assert.DoesNotContain(token, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-password", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", serialized, StringComparison.Ordinal);
    }
}

file static class Sample
{
    public const string PosterId = "POSTER-TEST-20261114-180000-11111111111111111111111111111111";

    public static TicketConfiguration Configuration() => new()
    {
        ApplicationId = 1,
        FormId = 1,
        TypeId = 1,
        StatusId = 1,
        PriorityId = 1,
        AccountId = 1
    };

    public static LookupSchedule Schedule(int attempts = 3, TimeSpan? interval = null) =>
        new(attempts, interval ?? TimeSpan.Zero);

    public static TdxClient Client(
        HttpMessageHandler handler,
        int attempts = 3,
        TimeSpan? interval = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(new Uri("https://tdx.example/tdx-test-base"), Configuration(), Schedule(attempts, interval), handler, delay ?? ((_, _) => Task.CompletedTask));

    public static RecordingHandler Handler(Func<RecordedCall, HttpResponseMessage> rest) =>
        new((call, _) => call.Uri.AbsolutePath.EndsWith("/auth", StringComparison.Ordinal)
            ? RecordingHandler.Json(HttpStatusCode.OK, "\"fake-token\"")
            : rest(call));

    public static bool IsCreate(RecordedCall call) =>
        call.Method == HttpMethod.Post && call.Uri.AbsolutePath.EndsWith("/tickets", StringComparison.Ordinal);

    public static string TicketArray(int id, string title) =>
        "[{" + "\"ID\":" + id + ",\"Title\":" + JsonSerializer.Serialize(title) + "}]";
}
