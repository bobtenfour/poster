using PosterPrintRequest.Tdx;

namespace PosterPrintRequest.TdxValidator;

public sealed class ValidatorInvocation
{
    public required string Host { get; init; }

    public required string BasePath { get; init; }

    public required string ReportsDirectory { get; init; }

    public required string SettingsPath { get; init; }

    public required TextReader Input { get; init; }

    public required TextWriter Output { get; init; }

    public Func<string>? ReadPassword { get; init; }

    /// <summary>Optional credential window. Returns null when the user cancels.</summary>
    public Func<(string UserName, string Password)?>? PromptCredentials { get; init; }

    public required Func<Uri, TicketConfiguration, LookupSchedule, ITdxSession> SessionFactory { get; init; }

    public Func<DateTimeOffset>? Clock { get; init; }

    public Func<Guid>? NewGuid { get; init; }

    public Func<TimeSpan, CancellationToken, Task>? Delay { get; init; }

    public static ValidatorInvocation Console() => new()
    {
        Host = SandboxHost.FixedHost,
        BasePath = SandboxHost.BasePath,
        ReportsDirectory = Path.Combine(AppContext.BaseDirectory, "reports"),
        SettingsPath = Path.Combine(AppContext.BaseDirectory, SettingsFile.FileName),
        Input = System.Console.In,
        Output = System.Console.Out,
        ReadPassword = MaskedInput.ReadPassword,
        PromptCredentials = OperatingSystem.IsWindows() ? WindowsCredentialPrompt.Prompt : null,
        SessionFactory = (uri, configuration, schedule) => new TdxClient(uri, configuration, schedule),
        Clock = () => DateTimeOffset.UtcNow,
        NewGuid = Guid.NewGuid,
        Delay = Task.Delay
    };
}

public static class ValidatorApplication
{
    public static async Task<int> RunAsync(ValidatorInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var output = invocation.Output;
        if (!string.Equals(invocation.Host, SandboxHost.FixedHost, StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine("The host is not the fixed sandbox host.");
            return 1;
        }

        var pathError = BasePathRules.Validate(invocation.BasePath);
        if (pathError is not null)
        {
            output.WriteLine(pathError);
            return 1;
        }

        Uri baseUri;
        try
        {
            baseUri = new UriBuilder(Uri.UriSchemeHttps, invocation.Host, 443, invocation.BasePath).Uri;
            TdxAddresses.ValidateBaseAddress(baseUri);
        }
        catch (Exception ex) when (ex is TdxAddressException or UriFormatException or ArgumentException)
        {
            output.WriteLine(ex.Message);
            return 1;
        }

        try
        {
            ProbeReports(invocation.ReportsDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            output.WriteLine("The reports folder is not writable. No TDX call was made.");
            return 1;
        }

        TicketSettings settings;
        try
        {
            settings = SettingsFile.Load(invocation.SettingsPath);
        }
        catch (Exception ex) when (ex is IOException or SettingsException or UnauthorizedAccessException)
        {
            output.WriteLine(ex.Message);
            return 1;
        }

        string userName;
        string password;
        if (invocation.PromptCredentials is not null)
        {
            output.WriteLine("Enter the TDX sandbox credentials in the Windows window.");
            var entered = invocation.PromptCredentials();
            if (entered is null)
            {
                output.WriteLine("Cancelled. No TDX call was made.");
                return 1;
            }

            (userName, password) = entered.Value;
        }
        else
        {
            output.Write("Username: ");
            userName = invocation.Input.ReadLine()?.Trim() ?? string.Empty;
            output.Write("Password: ");
            password = invocation.ReadPassword is null
                ? invocation.Input.ReadLine() ?? string.Empty
                : invocation.ReadPassword();
        }
        if (userName.Length == 0 || password.Length == 0)
        {
            output.WriteLine("Username and password are required.");
            return 1;
        }

        var clock = invocation.Clock ?? (() => DateTimeOffset.UtcNow);
        var newGuid = invocation.NewGuid ?? Guid.NewGuid;
        var started = clock();
        using var session = invocation.SessionFactory(baseUri, settings.Configuration, settings.Schedule);
        AuthResponseForm authForm;
        try
        {
            authForm = await session.AuthenticateAsync(userName, password, cancellationToken);
        }
        catch (Exception ex)
        {
            var failed = BuildReport(settings, started, string.Empty, authForm: null, first: null, second: null, verification: null, authError: ex.Message);
            ValidationReportWriter.Write(invocation.ReportsDirectory, started, newGuid, failed, new[] { userName, password }, output);
            output.WriteLine("Authentication failed. The report was written. No ticket was created.");
            return 2;
        }

        var posterId = TestPosterIds.Create(started, newGuid());
        CreationResult? first = null;
        CreationResult? second = null;
        LookupResult? verification = null;
        string? unexpected = null;
        string? lookupBlocker = null;
        var confirmationAttempts = 0;
        try
        {
            first = await session.CreateIfAbsentAsync(posterId, cancellationToken);
            if (first.Outcome == TdxOutcome.Created)
            {
                var confirmation = await ConfirmCreatedAsync(session, settings.Schedule, posterId, invocation.Delay ?? Task.Delay, cancellationToken);
                confirmationAttempts = confirmation.Attempts;
                if (confirmation.Found)
                {
                    second = await session.CreateIfAbsentAsync(posterId, cancellationToken);
                    verification = await session.LookupAsync(posterId, cancellationToken);
                }
                else
                {
                    lookupBlocker = "the lookup does not find a newly created ticket; duplicate prevention cannot be guaranteed";
                }
            }
        }
        catch (Exception ex)
        {
            unexpected = ex.Message;
        }

        var report = BuildReport(settings, started, posterId, authForm, first, second, verification, authError: null, unexpected, lookupBlocker, confirmationAttempts);
        var jsonPath = ValidationReportWriter.Write(invocation.ReportsDirectory, started, newGuid, report, new[] { userName, password }, output);
        output.WriteLine(report.Outcome);
        output.WriteLine(jsonPath);
        return report.Passed ? 0 : 2;
    }

    private static void ProbeReports(string directory)
    {
        Directory.CreateDirectory(directory);
        var probe = Path.Combine(directory, ".probe-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(probe, "probe");
        File.Delete(probe);
    }

    private static async Task<Confirmation> ConfirmCreatedAsync(
        ITdxSession session,
        LookupSchedule schedule,
        string posterId,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= schedule.Attempts; attempt++)
        {
            if (attempt > 1)
                await delay(schedule.Interval, cancellationToken);

            var lookup = await session.LookupAsync(posterId, cancellationToken);
            if (lookup.Succeeded && lookup.MatchingTicketIds.Count == 1)
                return new Confirmation(true, attempt);
        }

        return new Confirmation(false, schedule.Attempts);
    }

    private readonly record struct Confirmation(bool Found, int Attempts);

    private static ValidationReport BuildReport(
        TicketSettings settings,
        DateTimeOffset started,
        string posterId,
        AuthResponseForm? authForm,
        CreationResult? first,
        CreationResult? second,
        LookupResult? verification,
        string? authError,
        string? unexpected = null,
        string? lookupBlocker = null,
        int confirmationAttempts = 0)
    {
        var configuration = settings.Configuration;
        var title = posterId.Length == 0 ? string.Empty : TestTicketText.TitleFor(posterId);
        var steps = new List<StepRecord>();
        if (authError is not null)
        {
            steps.Add(new StepRecord { Name = "authenticate", Outcome = "Failed", Error = "Authentication failed: " + authError });
        }

        if (first is not null)
            steps.Add(Step("1. Create a ticket with a new test Poster ID", first));
        if (second is not null)
            steps.Add(Step("2. Repeat with the same Poster ID", second));
        else if (lookupBlocker is not null)
            steps.Add(new StepRecord { Name = "2. Repeat with the same Poster ID", Outcome = "Not run", Error = lookupBlocker + " Attempts: " + confirmationAttempts + "." });
        else if (first is not null && first.Outcome != TdxOutcome.Created)
            steps.Add(new StepRecord { Name = "2. Repeat with the same Poster ID", Outcome = "Not run", Error = "Step 1 did not create a ticket, so no further creation was attempted." });
        else if (unexpected is not null)
            steps.Add(new StepRecord { Name = "2. Repeat with the same Poster ID", Outcome = "Not run", Error = unexpected });

        if (verification is not null)
        {
            var count = verification.MatchingTicketIds.Count;
            var verified = verification.Succeeded && count == 1;
            steps.Add(new StepRecord
            {
                Name = "3. Verify that no second ticket was created",
                Outcome = verified ? "Verified" : "Failed",
                TicketId = count == 1 ? verification.MatchingTicketIds[0] : null,
                Error = verified ? null : verification.Error ?? $"Title matches: {count}.",
                Operations = new[] { verification.Evidence }
            });
        }
        else if (lookupBlocker is not null || unexpected is not null || (first is not null && first.Outcome != TdxOutcome.Created))
        {
            steps.Add(new StepRecord { Name = "3. Verify that no second ticket was created", Outcome = "Not run" });
        }

        if (unexpected is not null)
            steps.Add(new StepRecord { Name = "execution", Outcome = "Failed", Error = unexpected });

        var passed = first?.Outcome == TdxOutcome.Created
            && second?.Outcome == TdxOutcome.AlreadyExists
            && verification is not null
            && verification.Succeeded
            && verification.MatchingTicketIds.Count == 1;
        var uncertain = first?.Outcome == TdxOutcome.Uncertain || second?.Outcome == TdxOutcome.Uncertain;
        var blocker = first?.RequestorUidBlocker == true || second?.RequestorUidBlocker == true;
        var outcome = authError is not null || unexpected is not null || lookupBlocker is not null
            ? "Failed"
            : passed
                ? "Passed"
                : first?.Outcome.ToString() ?? "Failed";
        var blockerText = lookupBlocker
            ?? (blocker
                ? "The sandbox rejected creation because RequestorUid was missing. This is a blocker. No workaround was attempted."
                : null);

        return new ValidationReport
        {
            StartedAt = started,
            PosterId = posterId,
            Title = title,
            RequesterName = TestTicketText.RequesterName,
            RequestorCase = first?.RequestorCase ?? "AccountID from the settings file. No RequestorUid was sent.",
            RequestorUidBlocker = blocker,
            Blocker = blockerText,
            ApplicationId = configuration.ApplicationId,
            FormId = configuration.FormId,
            TypeId = configuration.TypeId,
            StatusId = configuration.StatusId,
            PriorityId = configuration.PriorityId,
            AccountId = configuration.AccountId,
            SourceId = configuration.SourceId,
            ResponsibleGroupId = configuration.ResponsibleGroupId,
            CustomAttributes = configuration.CustomAttributes,
            LookupAttempts = settings.Schedule.Attempts,
            LookupIntervalSeconds = settings.Schedule.Interval.TotalSeconds,
            NotificationParameters = NotificationSuppression.DisabledValues,
            AuthResponseForm = authForm?.ToString(),
            Steps = steps,
            Findings = Findings(configuration.ApplicationId, first, second, lookupBlocker, confirmationAttempts),
            Outcome = outcome,
            Passed = passed,
            ManualCheck = uncertain
                ? "The outcome is Uncertain. Check the sandbox manually. No further creation was attempted."
                : null
        };
    }

    private static StepRecord Step(string name, CreationResult result) => new()
    {
        Name = name,
        Outcome = result.Outcome.ToString(),
        TicketId = result.TicketId,
        Error = result.Error,
        Operations = result.Operations
    };

    private static FindingsRecord Findings(int applicationId, CreationResult? first, CreationResult? second, string? lookupBlocker, int confirmationAttempts)
    {
        var found = "Not observed in this execution.";
        if (lookupBlocker is not null)
        {
            found = $"No. The lookup schedule ended after {confirmationAttempts} attempts without a title match.";
        }
        else if (confirmationAttempts > 0 && second is not null)
        {
            found = $"Yes. The confirmation lookup found one title match on attempt {confirmationAttempts}.";
        }
        else if (first?.FoundByLookupAfterUncertainCreation is bool observed)
        {
            found = observed
                ? $"Yes. The lookup after the uncertain creation found one title match on attempt {first.LookupAttemptsAfterUncertainCreation}."
                : $"No. The lookup schedule ended after {first.LookupAttemptsAfterUncertainCreation} attempts without a title match.";
        }

        return new FindingsRecord
        {
            LookupOperation = $"POST api/{applicationId}/tickets/search. Source: public TeamDynamix Web API documentation for ticket search.",
            FieldsExamined = "SearchText was set to the Poster ID. The documented search text examines the title, the description, custom-attribute text, the reference code, and feed item text. A returned ticket counts as a match only when its Title contains the exact Poster ID.",
            SearchTextMatchOperator = "Not confirmed. The SearchText match operator is a sandbox finding. SearchText results alone are not treated as a match.",
            CreatedTicketFoundByLookup = found,
            CreationResponse = first?.CreationResponseFinding ?? "Not observed.",
            NotificationSuppression = "Not confirmed. EnableNotifyReviewer, NotifyRequestor, and NotifyResponsible were sent as false. Sending disabled values is not evidence that notification was suppressed.",
            Permissions = "Not confirmed. The sandbox account permissions are established only by observing this execution."
        };
    }
}
