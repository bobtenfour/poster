using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PosterPrintRequest.Tdx;

namespace PosterPrintRequest.TdxValidator;

public sealed class ValidationReport
{
    public DateTimeOffset StartedAt { get; init; }

    public string PosterId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string RequesterName { get; init; } = TestTicketText.RequesterName;

    public string RequestorCase { get; init; } = string.Empty;

    public bool RequestorUidBlocker { get; init; }

    public string? Blocker { get; init; }

    public int ApplicationId { get; init; }

    public int FormId { get; init; }

    public int TypeId { get; init; }

    public int StatusId { get; init; }

    public int PriorityId { get; init; }

    public int AccountId { get; init; }

    public int? SourceId { get; init; }

    public int? ResponsibleGroupId { get; init; }

    public IReadOnlyList<TicketAttribute> CustomAttributes { get; init; } = [];

    public int LookupAttempts { get; init; }

    public double LookupIntervalSeconds { get; init; }

    public IReadOnlyDictionary<string, bool> NotificationParameters { get; init; } =
        NotificationSuppression.DisabledValues;

    public string? AuthResponseForm { get; init; }

    public IReadOnlyList<StepRecord> Steps { get; init; } = [];

    public FindingsRecord Findings { get; init; } = new();

    public string Outcome { get; init; } = string.Empty;

    public bool Passed { get; init; }

    public string? ManualCheck { get; init; }
}

public sealed class StepRecord
{
    public string Name { get; init; } = string.Empty;

    public string Outcome { get; init; } = string.Empty;

    public int? TicketId { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<OperationEvidence> Operations { get; init; } = [];
}

public sealed class FindingsRecord
{
    public string LookupOperation { get; init; } = string.Empty;

    public string FieldsExamined { get; init; } = string.Empty;

    public string SearchTextMatchOperator { get; init; } = string.Empty;

    public string CreatedTicketFoundByLookup { get; init; } = string.Empty;

    public string CreationResponse { get; init; } = string.Empty;

    public string NotificationSuppression { get; init; } = string.Empty;

    public string Permissions { get; init; } = string.Empty;
}

public static class ValidationReportWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true
    };

    private static readonly Regex BearerValue = new(@"Bearer\s+\S+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string Write(
        string reportsDirectory,
        DateTimeOffset stamp,
        Func<Guid> newGuid,
        ValidationReport report,
        IReadOnlyCollection<string> secrets,
        TextWriter output)
    {
        var redacted = Redact(report, secrets);
        var json = JsonSerializer.Serialize(redacted, Json);
        var baseName = NextBaseName(reportsDirectory, stamp, newGuid);
        var jsonPath = Path.Combine(reportsDirectory, baseName + ".json");
        var textPath = Path.Combine(reportsDirectory, baseName + ".txt");
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(jsonPath, json, encoding);
        try
        {
            File.WriteAllText(textPath, Summary(redacted), encoding);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            output.WriteLine("The JSON report was written. The summary could not be written.");
        }

        return jsonPath;
    }

    public static string Summary(ValidationReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("TDX validation report");
        builder.AppendLine("Started at: " + report.StartedAt.ToString("O"));
        builder.AppendLine("Poster ID: " + report.PosterId);
        builder.AppendLine("Title: " + report.Title);
        builder.AppendLine("Requester name: " + report.RequesterName);
        builder.AppendLine("Requestor case: " + report.RequestorCase);
        builder.AppendLine("RequestorUid blocker: " + report.RequestorUidBlocker);
        if (!string.IsNullOrEmpty(report.Blocker))
            builder.AppendLine("Blocker: " + report.Blocker);
        builder.AppendLine("Application ID: " + report.ApplicationId);
        builder.AppendLine("Form ID: " + report.FormId);
        builder.AppendLine("Type ID: " + report.TypeId);
        builder.AppendLine("Status ID: " + report.StatusId);
        builder.AppendLine("Priority ID: " + report.PriorityId);
        builder.AppendLine("Account ID: " + report.AccountId);
        builder.AppendLine("Source ID: " + FormatOptional(report.SourceId));
        builder.AppendLine("Responsible group ID: " + FormatOptional(report.ResponsibleGroupId));
        builder.AppendLine("Custom attributes: " + report.CustomAttributes.Count);
        builder.AppendLine("Lookup attempts: " + report.LookupAttempts);
        builder.AppendLine("Lookup interval seconds: " + report.LookupIntervalSeconds);
        foreach (var parameter in report.NotificationParameters)
            builder.AppendLine(parameter.Key + ": " + parameter.Value);
        builder.AppendLine("Authentication response form: " + (report.AuthResponseForm ?? "not observed"));
        foreach (var step in report.Steps)
        {
            builder.AppendLine("Step: " + step.Name);
            builder.AppendLine("Step outcome: " + step.Outcome);
            builder.AppendLine("Ticket ID: " + FormatOptional(step.TicketId));
            if (!string.IsNullOrEmpty(step.Error))
                builder.AppendLine("Step error: " + step.Error);
            foreach (var operation in step.Operations)
            {
                builder.AppendLine("Operation: " + operation.Operation);
                builder.AppendLine("Operation time: " + operation.Timestamp.ToString("O"));
                builder.AppendLine("Method: " + operation.Method);
                builder.AppendLine("Relative path: " + operation.RelativePath);
                builder.AppendLine("Request payload: " + (operation.RequestPayload ?? string.Empty));
                builder.AppendLine("HTTP status: " + FormatOptional(operation.HttpStatus));
                builder.AppendLine("Response data: " + (operation.ResponseData ?? string.Empty));
                builder.AppendLine("Operation ticket ID: " + FormatOptional(operation.TicketId));
                if (!string.IsNullOrEmpty(operation.Error))
                    builder.AppendLine("Operation error: " + operation.Error);
            }
        }

        builder.AppendLine("Lookup operation: " + report.Findings.LookupOperation);
        builder.AppendLine("Fields examined: " + report.Findings.FieldsExamined);
        builder.AppendLine("SearchText match operator: " + report.Findings.SearchTextMatchOperator);
        builder.AppendLine("Created ticket found by lookup: " + report.Findings.CreatedTicketFoundByLookup);
        builder.AppendLine("Creation response: " + report.Findings.CreationResponse);
        builder.AppendLine("Notification suppression: " + report.Findings.NotificationSuppression);
        builder.AppendLine("Permissions: " + report.Findings.Permissions);
        builder.AppendLine("Outcome: " + report.Outcome);
        builder.AppendLine("Passed: " + report.Passed);
        if (!string.IsNullOrEmpty(report.ManualCheck))
            builder.AppendLine("Manual check: " + report.ManualCheck);

        return builder.ToString();
    }

    private static string NextBaseName(string reportsDirectory, DateTimeOffset stamp, Func<Guid> newGuid)
    {
        while (true)
        {
            var hex = newGuid().ToString("N")[..8];
            var name = $"tdx-validation-{stamp.ToUniversalTime():yyyyMMdd-HHmmss}-{hex}";
            if (!File.Exists(Path.Combine(reportsDirectory, name + ".json"))
                && !File.Exists(Path.Combine(reportsDirectory, name + ".txt")))
                return name;
        }
    }

    private static ValidationReport Redact(ValidationReport report, IReadOnlyCollection<string> secrets) => new()
    {
        StartedAt = report.StartedAt,
        PosterId = Clean(report.PosterId, secrets),
        Title = Clean(report.Title, secrets),
        RequesterName = Clean(report.RequesterName, secrets),
        RequestorCase = Clean(report.RequestorCase, secrets),
        RequestorUidBlocker = report.RequestorUidBlocker,
        Blocker = Clean(report.Blocker, secrets),
        ApplicationId = report.ApplicationId,
        FormId = report.FormId,
        TypeId = report.TypeId,
        StatusId = report.StatusId,
        PriorityId = report.PriorityId,
        AccountId = report.AccountId,
        SourceId = report.SourceId,
        ResponsibleGroupId = report.ResponsibleGroupId,
        CustomAttributes = report.CustomAttributes.Select(attribute => new TicketAttribute
        {
            Id = attribute.Id,
            Value = Clean(attribute.Value, secrets)
        }).ToList(),
        LookupAttempts = report.LookupAttempts,
        LookupIntervalSeconds = report.LookupIntervalSeconds,
        NotificationParameters = report.NotificationParameters,
        AuthResponseForm = Clean(report.AuthResponseForm, secrets),
        Steps = report.Steps.Select(step => new StepRecord
        {
            Name = Clean(step.Name, secrets),
            Outcome = Clean(step.Outcome, secrets),
            TicketId = step.TicketId,
            Error = Clean(step.Error, secrets),
            Operations = step.Operations.Select(operation => new OperationEvidence
            {
                Timestamp = operation.Timestamp,
                Operation = Clean(operation.Operation, secrets),
                Method = Clean(operation.Method, secrets),
                RelativePath = Clean(operation.RelativePath, secrets),
                RequestPayload = Clean(operation.RequestPayload, secrets),
                HttpStatus = operation.HttpStatus,
                ResponseData = Clean(operation.ResponseData, secrets),
                TicketId = operation.TicketId,
                Error = Clean(operation.Error, secrets)
            }).ToList()
        }).ToList(),
        Findings = new FindingsRecord
        {
            LookupOperation = Clean(report.Findings.LookupOperation, secrets),
            FieldsExamined = Clean(report.Findings.FieldsExamined, secrets),
            SearchTextMatchOperator = Clean(report.Findings.SearchTextMatchOperator, secrets),
            CreatedTicketFoundByLookup = Clean(report.Findings.CreatedTicketFoundByLookup, secrets),
            CreationResponse = Clean(report.Findings.CreationResponse, secrets),
            NotificationSuppression = Clean(report.Findings.NotificationSuppression, secrets),
            Permissions = Clean(report.Findings.Permissions, secrets)
        },
        Outcome = Clean(report.Outcome, secrets),
        Passed = report.Passed,
        ManualCheck = Clean(report.ManualCheck, secrets)
    };

    [return: NotNullIfNotNull(nameof(value))]
    private static string? Clean(string? value, IReadOnlyCollection<string> secrets)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var text = value;
        foreach (var secret in secrets.OrderByDescending(secret => secret.Length))
        {
            if (secret.Length == 0)
                continue;
            text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
        }

        return BearerValue.Replace(text, "[redacted]");
    }

    private static string FormatOptional(int? value) => value?.ToString() ?? "none";
}
