using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Tdx;
using PosterPrintRequest.Web.Requests;
using PosterPrintRequest.Web.Tdx;

namespace PosterPrintRequest.Tests.Tdx;

/// <summary>TDX Integration Contract sections 5, 6, 8, 8A, 9 and 14.2a, with a simulated store and connection only.</summary>
public sealed class TdxTicketProcessorTests
{
    private static readonly TdxOptions Options = new()
    {
        BaseUrl = new Uri("https://tdx.example.test/SBWebApi/"),
        Ticket = new TicketConfiguration { ApplicationId = 1, FormId = 2, TypeId = 3, StatusId = 4, PriorityId = 5, AccountId = 6, ResponsibleGroupId = 7 },
        Lookups = new LookupSchedule(2, TimeSpan.FromMilliseconds(1)),
        CredentialFile = "unused",
        RequestTimeout = TimeSpan.FromSeconds(5)
    };

    [Fact]
    public async Task Two_concurrent_processors_send_exactly_one_creation()
    {
        var store = new FakeStore();
        store.Add(1, TdxTicketStates.Pending);
        var tdx = new FakeTdx { CreateDelay = TimeSpan.FromMilliseconds(200) };
        var first = Processor(tdx);
        var second = Processor(tdx);

        await Task.WhenAll(
            Task.Run(() => first.ProcessOneAsync(store, 1, CancellationToken.None)),
            Task.Run(() => second.ProcessOneAsync(store, 1, CancellationToken.None)));

        Assert.Equal(1, tdx.CreateCalls);
        Assert.Equal(TdxTicketStates.Created, store.Row(1).State);
        Assert.Equal(5000, store.Row(1).TicketId);
        Assert.Null(store.Row(1).ClaimedUntil);
    }

    [Fact]
    public async Task The_marker_is_committed_before_the_creation_is_sent()
    {
        var store = new FakeStore();
        store.Add(1, TdxTicketStates.Pending);
        string? stateWhenSent = null;
        var tdx = new FakeTdx { OnCreate = () => stateWhenSent = store.Row(1).State };

        await Processor(tdx).ProcessOneAsync(store, 1, CancellationToken.None);

        Assert.Equal(TdxTicketStates.Uncertain, stateWhenSent);
        Assert.Equal(TdxTicketStates.Created, store.Row(1).State);
    }

    [Fact]
    public async Task A_crash_after_the_marker_leaves_Uncertain_and_Uncertain_never_creates()
    {
        var store = new FakeStore();
        store.Add(1, TdxTicketStates.Pending);
        var tdx = new FakeTdx { OnCreate = () => throw new InvalidOperationException("crash after create") };

        await Processor(tdx).ProcessOneAsync(store, 1, CancellationToken.None);
        Assert.Equal(TdxTicketStates.Uncertain, store.Row(1).State);
        Assert.Equal(1, tdx.CreateCalls);

        tdx.OnCreate = null;
        tdx.LookupIds = [];
        await Processor(tdx).ProcessOneAsync(store, 1, CancellationToken.None);
        Assert.Equal(1, tdx.CreateCalls);
        Assert.Equal(TdxTicketStates.Uncertain, store.Row(1).State);
        Assert.Null(store.Row(1).ClaimedUntil);

        tdx.LookupIds = [7777];
        await Processor(tdx).ProcessOneAsync(store, 1, CancellationToken.None);
        Assert.Equal(1, tdx.CreateCalls);
        Assert.Equal(TdxTicketStates.Created, store.Row(1).State);
        Assert.Equal(7777, store.Row(1).TicketId);
    }

    [Fact]
    public async Task A_readable_4xx_returns_the_request_to_Pending()
    {
        var store = new FakeStore();
        store.Add(1, TdxTicketStates.Pending);
        var tdx = new FakeTdx { CreateResult = (TdxOutcome.Failed, null, true) };

        await Processor(tdx).ProcessOneAsync(store, 1, CancellationToken.None);

        Assert.Equal(TdxTicketStates.Pending, store.Row(1).State);
        Assert.Null(store.Row(1).TicketId);
    }

    [Fact]
    public async Task An_uncertain_creation_result_stays_Uncertain()
    {
        var store = new FakeStore();
        store.Add(1, TdxTicketStates.Pending);
        var tdx = new FakeTdx { CreateResult = (TdxOutcome.Uncertain, null, false) };

        await Processor(tdx).ProcessOneAsync(store, 1, CancellationToken.None);

        Assert.Equal(TdxTicketStates.Uncertain, store.Row(1).State);
    }

    [Fact]
    public async Task A_lost_claim_sends_no_creation()
    {
        var store = new FakeStore { LoseClaimBeforeMarker = true };
        store.Add(1, TdxTicketStates.Pending);
        var tdx = new FakeTdx();

        await Processor(tdx).ProcessOneAsync(store, 1, CancellationToken.None);

        Assert.Equal(0, tdx.CreateCalls);
    }

    [Fact]
    public async Task A_claimed_request_is_not_touched_by_another_processor()
    {
        var store = new FakeStore();
        store.Add(1, TdxTicketStates.Pending);
        store.Row(1).ClaimedUntil = DateTime.UtcNow.AddMinutes(10);
        var tdx = new FakeTdx();

        await Processor(tdx).ProcessOneAsync(store, 1, CancellationToken.None);

        Assert.Equal(0, tdx.AuthCalls);
        Assert.Equal(TdxTicketStates.Pending, store.Row(1).State);
    }

    [Fact]
    public async Task A_people_search_failure_keeps_Pending_without_creation()
    {
        var store = new FakeStore();
        store.Add(1, TdxTicketStates.Pending);
        var tdx = new FakeTdx { Requestor = new RequestorResolution { Retry = true } };

        await Processor(tdx).ProcessOneAsync(store, 1, CancellationToken.None);

        Assert.Equal(0, tdx.CreateCalls);
        Assert.Equal(TdxTicketStates.Pending, store.Row(1).State);
        Assert.Null(store.Row(1).ClaimedUntil);
    }

    [Fact]
    public async Task A_missing_credential_keeps_Pending()
    {
        var store = new FakeStore();
        store.Add(1, TdxTicketStates.Pending);
        var tdx = new FakeTdx();
        var processor = new TdxTicketProcessor(
            new FakeFactory(tdx),
            () => throw new TdxCredentialException("The TDX credential file is missing."),
            Options,
            TimeProvider.System,
            NullLogger.Instance);

        await processor.ProcessOneAsync(store, 1, CancellationToken.None);

        Assert.Equal(TdxTicketStates.Pending, store.Row(1).State);
        Assert.Null(store.Row(1).ClaimedUntil);
        Assert.Equal(0, tdx.CreateCalls);
    }

    [Fact]
    public void The_ticket_carries_the_contract_title_description_requestor_and_account()
    {
        var work = new TdxWork(1, "POSTER-2026-00042", "  Caries in mice ", "Ada Lovelace", "ada@example.edu", TdxTicketStates.Pending, DateTime.UtcNow);
        var uid = Guid.NewGuid();

        var found = TdxTicketProcessor.BuildRequest(work, new RequestorResolution { RequestorUid = uid, AccountId = 99 });
        var none = TdxTicketProcessor.BuildRequest(work, new RequestorResolution());

        Assert.Equal("POSTER-2026-00042 \u2014 Caries in mice", found.Title);
        Assert.Equal("Requester: Ada Lovelace", found.Description);
        Assert.Equal(uid, found.RequestorUid);
        Assert.Equal(99, found.AccountId);
        Assert.True(found.DisallowRequestorCreation);
        Assert.Equal(TicketMatch.TitleStartsWithPosterId, found.Match);
        Assert.Null(none.RequestorUid);
        Assert.Null(none.AccountId);
        Assert.True(found.IsMatch("POSTER-2026-00042 \u2014 anything"));
        Assert.False(found.IsMatch("POSTER-2026-000421 \u2014 other"));
    }

    [Fact]
    public void The_lease_is_longer_than_the_attempt_budget()
    {
        var processor = Processor(new FakeTdx());
        Assert.True(processor.Lease > processor.AttemptBudget);
    }

    [Fact]
    public void Options_are_read_from_configuration_and_disabled_when_incomplete()
    {
        var complete = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tdx:BaseUrl"] = "https://tdx.example.test/SBWebApi",
            ["Tdx:ApplicationId"] = "1",
            ["Tdx:FormId"] = "2",
            ["Tdx:TypeId"] = "3",
            ["Tdx:StatusId"] = "4",
            ["Tdx:PriorityId"] = "5",
            ["Tdx:AccountId"] = "6",
            ["Tdx:ResponsibleGroupId"] = "7",
            ["Tdx:CredentialFile"] = "C:\\Secrets\\Tdx\\tdx.bin"
        }).Build();
        var options = TdxOptions.Read(complete, out var problem);
        Assert.NotNull(options);
        Assert.Null(problem);
        Assert.Equal(7, options.Ticket.ResponsibleGroupId);
        Assert.Equal(3, options.Lookups.Attempts);

        var missing = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tdx:BaseUrl"] = "http://tdx.example.test/SBWebApi",
            ["Tdx:ApplicationId"] = "x"
        }).Build();
        Assert.Null(TdxOptions.Read(missing, out problem));
        Assert.Contains("BaseUrl", problem);
        Assert.Contains("ApplicationId", problem);
        Assert.Contains("CredentialFile", problem);
        Assert.DoesNotContain("tdx.example.test", problem);

        Assert.Null(TdxOptions.Read(new ConfigurationBuilder().Build(), out problem));
    }

    [Fact]
    public void The_credential_json_needs_user_name_and_password_and_errors_hide_content()
    {
        var value = TdxCredential.Parse(Encoding.UTF8.GetBytes("{\"UserName\":\"svc\",\"Password\":\"p\"}"));
        Assert.Equal("svc", value.UserName);
        Assert.Equal("TdxCredential", value.ToString());

        var error = Assert.Throws<TdxCredentialException>(() => TdxCredential.Parse(Encoding.UTF8.GetBytes("{\"UserName\":\"secret-user\"}")));
        Assert.DoesNotContain("secret-user", error.Message);
        Assert.Throws<TdxCredentialException>(() => TdxCredential.Parse(Encoding.UTF8.GetBytes("not json")));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("A valid title", true)]
    [InlineData("Line\nbreak", false)]
    [InlineData("Tab\there", false)]
    public void Poster_title_rules(string title, bool valid)
    {
        Assert.Equal(valid, PosterTitleRules.Check(title) is null);
    }

    [Fact]
    public void Poster_title_is_at_most_200_characters()
    {
        Assert.Null(PosterTitleRules.Check(new string('a', 200)));
        Assert.NotNull(PosterTitleRules.Check(new string('a', 201)));
        Assert.Null(PosterTitleRules.Check("  " + new string('a', 200) + "  "));
    }

    private static TdxTicketProcessor Processor(FakeTdx tdx) =>
        new(new FakeFactory(tdx), () => new TdxCredentialValue("user", "password"), Options, TimeProvider.System, NullLogger.Instance);

    private sealed class Row
    {
        public string State { get; set; } = TdxTicketStates.Pending;

        public int? TicketId { get; set; }

        public DateTime? ClaimedUntil { get; set; }
    }

    /// <summary>Same conditions as the SQL statements of TdxTicketStore, each applied atomically.</summary>
    private sealed class FakeStore : ITdxTicketStore
    {
        private readonly Dictionary<int, Row> _rows = new();
        private readonly object _sync = new();

        public bool LoseClaimBeforeMarker { get; set; }

        public void Add(int id, string state) => _rows[id] = new Row { State = state };

        public Row Row(int id) => _rows[id];

        public Task<IReadOnlyList<int>> DueAsync(DateTime nowUtc, int max, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                IReadOnlyList<int> due = _rows.Where(pair => Claimable(pair.Value, nowUtc)).Select(pair => pair.Key).Take(max).ToList();
                return Task.FromResult(due);
            }
        }

        public Task<TdxWork?> ClaimAsync(int posterRequestId, DateTime nowUtc, DateTime claimUntil, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                var row = _rows[posterRequestId];
                if (!Claimable(row, nowUtc))
                {
                    return Task.FromResult<TdxWork?>(null);
                }

                row.ClaimedUntil = claimUntil;
                return Task.FromResult<TdxWork?>(new TdxWork(posterRequestId, "POSTER-2026-0000" + posterRequestId, "Title", "Name", "a@example.edu", row.State, claimUntil));
            }
        }

        public Task<bool> MarkUncertainAsync(TdxWork work, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (LoseClaimBeforeMarker)
                {
                    _rows[work.PosterRequestId].ClaimedUntil = null;
                }

                if (!Holds(work))
                {
                    return Task.FromResult(false);
                }

                _rows[work.PosterRequestId].State = TdxTicketStates.Uncertain;
                return Task.FromResult(true);
            }
        }

        public Task<bool> CompleteCreatedAsync(TdxWork work, int ticketId, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!Holds(work))
                {
                    return Task.FromResult(false);
                }

                var row = _rows[work.PosterRequestId];
                row.TicketId = ticketId;
                row.State = TdxTicketStates.Created;
                row.ClaimedUntil = null;
                return Task.FromResult(true);
            }
        }

        public Task<bool> ReleaseAsync(TdxWork work, string state, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!Holds(work))
                {
                    return Task.FromResult(false);
                }

                var row = _rows[work.PosterRequestId];
                row.State = state;
                row.ClaimedUntil = null;
                return Task.FromResult(true);
            }
        }

        private static bool Claimable(Row row, DateTime nowUtc) =>
            row.TicketId is null
            && (row.State == TdxTicketStates.Pending || row.State == TdxTicketStates.Uncertain)
            && (row.ClaimedUntil is null || row.ClaimedUntil < nowUtc);

        private bool Holds(TdxWork work)
        {
            var row = _rows[work.PosterRequestId];
            return row.TicketId is null && row.ClaimedUntil == work.ClaimedUntil;
        }
    }

    private sealed class FakeFactory(FakeTdx tdx) : ITdxConnectionFactory
    {
        public ITdxConnection Open() => tdx;
    }

    /// <summary>Simulated TDX. The creation follows the library's order: lookup, then the callback, then POST.</summary>
    private sealed class FakeTdx : ITdxConnection
    {
        private int _createCalls;
        private int _authCalls;

        public int CreateCalls => _createCalls;

        public int AuthCalls => _authCalls;

        public TimeSpan CreateDelay { get; set; }

        public Action? OnCreate { get; set; }

        public IReadOnlyList<int> LookupIds { get; set; } = [];

        public RequestorResolution Requestor { get; set; } = new();

        public (TdxOutcome Outcome, int? TicketId, bool Rejected)? CreateResult { get; set; }

        public Task AuthenticateAsync(TdxCredentialValue credential, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _authCalls);
            return Task.CompletedTask;
        }

        public Task<RequestorResolution> ResolveRequestorAsync(string? email, string? name, CancellationToken cancellationToken) =>
            Task.FromResult(Requestor);

        public Task<LookupResult> LookupAsync(TicketRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new LookupResult { Succeeded = true, MatchingTicketIds = LookupIds });

        public async Task<CreationResult> CreateIfAbsentAsync(TicketRequest request, Func<CancellationToken, Task> beforeCreate, CancellationToken cancellationToken)
        {
            if (LookupIds.Count == 1)
            {
                return new CreationResult { Outcome = TdxOutcome.AlreadyExists, TicketId = LookupIds[0] };
            }

            await beforeCreate(cancellationToken);
            Interlocked.Increment(ref _createCalls);
            await Task.Delay(CreateDelay, cancellationToken);
            OnCreate?.Invoke();
            var result = CreateResult is { } custom
                ? new CreationResult { Outcome = custom.Outcome, TicketId = custom.TicketId }
                : new CreationResult { Outcome = TdxOutcome.Created, TicketId = 5000 };
            Sent(result, CreateResult?.Rejected ?? false);
            LookupIds = result.TicketId is int id ? [id] : LookupIds;
            return result;
        }

        public void Dispose()
        {
        }

        private static void Sent(CreationResult result, bool rejected)
        {
            typeof(CreationResult).GetProperty(nameof(CreationResult.CreateSent))!.SetValue(result, true);
            typeof(CreationResult).GetProperty(nameof(CreationResult.CreateRejected))!.SetValue(result, rejected);
        }
    }
}
