using System.Net;
using PosterPrintRequest.Tdx;
using PosterPrintRequest.TdxValidator;

namespace PosterPrintRequest.Tdx.Tests;

public class ValidatorAcceptanceTests
{
    [Fact]
    public void Projects_reference_only_the_tdx_client_and_validator()
    {
        var root = RepoRoot();
        var library = File.ReadAllText(Path.Combine(root, "src", "PosterPrintRequest.Tdx", "PosterPrintRequest.Tdx.csproj"));
        var validator = File.ReadAllText(Path.Combine(root, "src", "PosterPrintRequest.TdxValidator", "PosterPrintRequest.TdxValidator.csproj"));
        var tests = File.ReadAllText(Path.Combine(root, "tests", "PosterPrintRequest.Tdx.Tests", "PosterPrintRequest.Tdx.Tests.csproj"));

        Assert.DoesNotContain("ProjectReference", library, StringComparison.Ordinal);
        Assert.Contains("PosterPrintRequest.Tdx.csproj", validator, StringComparison.Ordinal);
        Assert.Contains("PosterPrintRequest.Tdx.csproj", tests, StringComparison.Ordinal);
        Assert.Contains("PosterPrintRequest.TdxValidator.csproj", tests, StringComparison.Ordinal);
        foreach (var project in new[] { library, validator, tests })
        {
            Assert.DoesNotContain("PosterPrintRequest.Web", project, StringComparison.Ordinal);
            Assert.DoesNotContain("PosterPrintRequest.Domain", project, StringComparison.Ordinal);
            Assert.DoesNotContain("PosterPrintRequest.Infrastructure", project, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Validator_source_has_no_http_or_socket_type_and_keeps_the_placeholder()
    {
        var root = RepoRoot();
        var validatorDir = Path.Combine(root, "src", "PosterPrintRequest.TdxValidator");
        var libraryDir = Path.Combine(root, "src", "PosterPrintRequest.Tdx");
        var validatorSource = string.Join('\n', Directory.GetFiles(validatorDir, "*.cs").Select(File.ReadAllText));
        var librarySource = string.Join('\n', Directory.GetFiles(libraryDir, "*.cs").Select(File.ReadAllText));

        Assert.DoesNotContain("HttpClient", validatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpMessageHandler", validatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Socket", validatorSource, StringComparison.Ordinal);
        Assert.Contains("\"<TDX_SANDBOX_API_PATH>\"", validatorSource, StringComparison.Ordinal);
        Assert.Contains(SandboxHost.FixedHost, validatorSource, StringComparison.Ordinal);
        Assert.Equal("help.uillinois.edu", SandboxHost.FixedHost);
        Assert.DoesNotContain("help.uillinois.edu", librarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("sbTDWebAPI", librarySource, StringComparison.Ordinal);
        Assert.DoesNotContain("sbTDWebAPI", validatorSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unreplaced_placeholder_stops_before_http()
    {
        var called = false;
        var code = await RunAsync((_, _, _) =>
        {
            called = true;
            throw new InvalidOperationException("HTTP was reached.");
        }, basePath: "<TDX_SANDBOX_API_PATH>");

        Assert.Equal(1, code);
        Assert.False(called);
    }

    [Theory]
    [InlineData("relative-path")]
    [InlineData("/tdx-test-base/<TDX_SANDBOX_API_PATH>")]
    [InlineData("/tdx-test-base/../outside")]
    [InlineData("/tdx-test-base/%2e%2e/outside")]
    [InlineData("/tdx-test-base/%2fother")]
    [InlineData("/tdx-test-base?x=1")]
    public async Task Invalid_base_path_stops_before_http(string basePath)
    {
        var called = false;
        var code = await RunAsync((_, _, _) =>
        {
            called = true;
            throw new InvalidOperationException("HTTP was reached.");
        }, basePath: basePath);

        Assert.Equal(1, code);
        Assert.False(called);
    }

    [Fact]
    public async Task Host_other_than_the_fixed_host_stops_before_http()
    {
        var called = false;
        var code = await RunAsync((_, _, _) =>
        {
            called = true;
            throw new InvalidOperationException("HTTP was reached.");
        }, host: "example.test");

        Assert.Equal(1, code);
        Assert.False(called);
    }

    [Fact]
    public async Task Unwritable_reports_folder_stops_before_http()
    {
        var file = Path.Combine(Path.GetTempPath(), "tdx-not-a-directory-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(file, "x");
        var called = false;
        try
        {
            var code = await RunAsync((_, _, _) =>
            {
                called = true;
                throw new InvalidOperationException("HTTP was reached.");
            }, reports: Path.Combine(file, "reports"));

            Assert.Equal(1, code);
            Assert.False(called);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Invalid_settings_stop_before_http()
    {
        var root = TempRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "tdx-validator.settings.json"), "{\"applicationId\":\"<APPLICATION_ID>\"}");
        var called = false;
        var code = await RunAsync((_, _, _) =>
        {
            called = true;
            throw new InvalidOperationException("HTTP was reached.");
        }, root: root);

        Assert.Equal(1, code);
        Assert.False(called);
    }

    [Fact]
    public void Two_test_poster_ids_in_the_same_second_differ()
    {
        var timestamp = new DateTimeOffset(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);
        var first = TestPosterIds.Create(timestamp, Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var second = TestPosterIds.Create(timestamp, Guid.Parse("22222222-2222-2222-2222-222222222222"));

        Assert.NotEqual(first, second);
        Assert.Equal(60, first.Length);
        Assert.StartsWith("POSTER-TEST-20261114-180000-", first, StringComparison.Ordinal);
        Assert.DoesNotMatch("^POSTER-[0-9]{4}-[0-9]{6}$", first);
    }

    [Fact]
    public async Task Reports_omit_credentials_and_authentication_headers()
    {
        var root = TempRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "tdx-validator.settings.json"), ValidSettings);
        var session = new FakeSession();
        var code = await RunAsync((_, _, _) => session, root: root, userName: "fake-user", password: "super-secret-password");
        var json = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "reports"), "*.json").Single());
        var text = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "reports"), "*.txt").Single());

        Assert.Equal(0, code);
        Assert.Equal("fake-user", session.SeenUser);
        Assert.Equal("super-secret-password", session.SeenPassword);
        foreach (var report in new[] { json, text })
        {
            Assert.DoesNotContain("super-secret-password", report, StringComparison.Ordinal);
            Assert.DoesNotContain("fake-user", report, StringComparison.Ordinal);
            Assert.DoesNotContain("Authorization", report, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer ", report, StringComparison.Ordinal);
            Assert.Contains("Integration Test", report, StringComparison.Ordinal);
            Assert.Contains("JsonString", report, StringComparison.Ordinal);
            Assert.Contains("EnableNotifyReviewer", report, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Created_ticket_not_found_within_the_schedule_blocks_the_repeat()
    {
        var root = TempRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "tdx-validator.settings.json"), ValidSettings);
        var session = new UnsearchableSession();
        var code = await RunAsync((_, _, _) => session, root: root);
        var json = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "reports"), "*.json").Single());

        Assert.Equal(2, code);
        Assert.Equal(1, session.Creates);
        Assert.Equal(3, session.Lookups);
        Assert.Contains("the lookup does not find a newly created ticket; duplicate prevention cannot be guaranteed", json, StringComparison.Ordinal);
        Assert.Contains("Attempts: 3", json, StringComparison.Ordinal);
        Assert.Contains("after 3 attempts", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_keeps_authorization_denied_and_redacts_the_username()
    {
        var root = TempRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "tdx-validator.settings.json"), ValidSettings);
        const string userName = "poster-user-77";
        const string password = "super-secret-password";
        using var handler = new RecordingHandler((call, _) =>
        {
            if (call.Uri.AbsolutePath.EndsWith("/auth", StringComparison.Ordinal))
                return RecordingHandler.Json(HttpStatusCode.OK, "\"fake-token\"");
            if (call.Uri.AbsolutePath.EndsWith("/search", StringComparison.Ordinal))
                return RecordingHandler.Json(HttpStatusCode.OK, "[]");

            return RecordingHandler.Json(
                HttpStatusCode.Unauthorized,
                "Authorization has been denied for this request. User " + userName + " Bearer secret-token-value");
        });
        var code = await RunAsync(
            (uri, configuration, schedule) => new TdxClient(uri, configuration, schedule, handler, (_, _) => Task.CompletedTask),
            root: root,
            userName: userName,
            password: password);
        var json = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "reports"), "*.json").Single());
        var text = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "reports"), "*.txt").Single());

        Assert.Equal(2, code);
        foreach (var report in new[] { json, text })
        {
            Assert.Contains("Authorization has been denied for this request", report, StringComparison.Ordinal);
            Assert.DoesNotContain(userName, report, StringComparison.Ordinal);
            Assert.DoesNotContain(password, report, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-token-value", report, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer ", report, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Unexpected_exception_still_writes_a_failed_report()
    {
        var root = TempRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "tdx-validator.settings.json"), ValidSettings);
        var code = await RunAsync((_, _, _) => new ThrowingSession(), root: root, userName: "poster-user-77", password: "super-secret-password");
        var json = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "reports"), "*.json").Single());

        Assert.Equal(2, code);
        Assert.Contains("\"outcome\": \"Failed\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lookup exploded", json, StringComparison.Ordinal);
        Assert.DoesNotContain("poster-user-77", json, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-password", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelled_credential_window_stops_before_http()
    {
        var root = TempRoot();
        await File.WriteAllTextAsync(Path.Combine(root, "tdx-validator.settings.json"), ValidSettings);
        var called = false;
        var code = await RunAsync((_, _, _) =>
        {
            called = true;
            throw new InvalidOperationException("HTTP was reached.");
        }, root: root, prompt: () => null);

        Assert.Equal(1, code);
        Assert.False(called);
    }

    private static async Task<int> RunAsync(
        Func<Uri, TicketConfiguration, LookupSchedule, ITdxSession> sessionFactory,
        string? root = null,
        string? reports = null,
        string? basePath = null,
        string? host = null,
        string userName = "fake-user",
        string password = "fake-password",
        Func<(string UserName, string Password)?>? prompt = null)
    {
        root ??= TempRoot();
        var invocation = new ValidatorInvocation
        {
            Host = host ?? SandboxHost.FixedHost,
            BasePath = basePath ?? "/tdx-test-base",
            ReportsDirectory = reports ?? Path.Combine(root, "reports"),
            SettingsPath = Path.Combine(root, "tdx-validator.settings.json"),
            Input = new StringReader(userName + Environment.NewLine),
            Output = new StringWriter(),
            ReadPassword = () => password,
            PromptCredentials = prompt,
            SessionFactory = sessionFactory,
            Clock = () => new DateTimeOffset(2026, 11, 14, 18, 0, 0, TimeSpan.Zero),
            NewGuid = Guid.NewGuid,
            Delay = (_, _) => Task.CompletedTask
        };
        return await ValidatorApplication.RunAsync(invocation, CancellationToken.None);
    }

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tdx-validator-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PosterPrintRequest.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("The solution root was not found.");
    }

    private const string ValidSettings = """
        {
          "applicationId": 1,
          "formId": 1,
          "typeId": 1,
          "statusId": 1,
          "priorityId": 1,
          "accountId": 1,
          "sourceId": null,
          "responsibleGroupId": null,
          "customAttributes": [],
          "lookupAttempts": 3,
          "lookupIntervalSeconds": 5
        }
        """;

    private sealed class FakeSession : ITdxSession
    {
        private readonly HashSet<string> _created = new();

        public string? SeenUser { get; private set; }

        public string? SeenPassword { get; private set; }

        public Task<AuthResponseForm> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
        {
            SeenUser = userName;
            SeenPassword = password;
            return Task.FromResult(AuthResponseForm.JsonString);
        }

        public Task<CreationResult> CreateIfAbsentAsync(string posterId, CancellationToken cancellationToken)
        {
            var first = _created.Add(posterId);
            return Task.FromResult(new CreationResult
            {
                Outcome = first ? TdxOutcome.Created : TdxOutcome.AlreadyExists,
                PosterId = posterId,
                Title = TestTicketText.TitleFor(posterId),
                RequesterName = TestTicketText.RequesterName,
                TicketId = 100,
                Operations =
                [
                    new OperationEvidence
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Operation = first ? "create" : "lookup",
                        Method = "POST",
                        RelativePath = "api/1/tickets",
                        RequestPayload = "{\"AccountID\":1,\"Description\":\"Integration Test\"}",
                        HttpStatus = first ? 201 : 200,
                        ResponseData = "Ticket.ID 100.",
                        TicketId = 100
                    }
                ],
                CreationResponseFinding = first ? "HTTP 201 returned Ticket.ID." : "Not observed. No creation was sent."
            });
        }

        public Task<LookupResult> LookupAsync(string posterId, CancellationToken cancellationToken) =>
            Task.FromResult(new LookupResult
            {
                Succeeded = true,
                MatchingTicketIds = [100],
                ReturnedTicketCount = 1,
                Evidence = new OperationEvidence
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Operation = "lookup",
                    Method = "POST",
                    RelativePath = "api/1/tickets/search",
                    RequestPayload = "{\"SearchText\":\"" + posterId + "\"}",
                    HttpStatus = 200,
                    ResponseData = "Returned 1. Title matches 1.",
                    TicketId = 100
                }
            });

        public void Dispose()
        {
        }
    }

    private sealed class UnsearchableSession : ITdxSession
    {
        public int Creates { get; private set; }

        public int Lookups { get; private set; }

        public Task<AuthResponseForm> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken) =>
            Task.FromResult(AuthResponseForm.JsonString);

        public Task<CreationResult> CreateIfAbsentAsync(string posterId, CancellationToken cancellationToken)
        {
            Creates++;
            return Task.FromResult(new CreationResult
            {
                Outcome = TdxOutcome.Created,
                PosterId = posterId,
                Title = TestTicketText.TitleFor(posterId),
                TicketId = 100,
                CreationResponseFinding = "HTTP 201 returned Ticket.ID."
            });
        }

        public Task<LookupResult> LookupAsync(string posterId, CancellationToken cancellationToken)
        {
            Lookups++;
            return Task.FromResult(new LookupResult
            {
                Succeeded = true,
                MatchingTicketIds = new List<int>(),
                Evidence = new OperationEvidence
                {
                    Operation = "lookup",
                    Method = "POST",
                    RelativePath = "api/1/tickets/search",
                    HttpStatus = 200,
                    ResponseData = "Returned 0. Title matches 0."
                }
            });
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingSession : ITdxSession
    {
        public Task<AuthResponseForm> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken) =>
            Task.FromResult(AuthResponseForm.JsonString);

        public Task<CreationResult> CreateIfAbsentAsync(string posterId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("lookup exploded for poster-user-77 super-secret-password");

        public Task<LookupResult> LookupAsync(string posterId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("not used");

        public void Dispose()
        {
        }
    }
}
