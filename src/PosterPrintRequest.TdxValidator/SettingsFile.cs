using System.Text.Json;
using PosterPrintRequest.Tdx;

namespace PosterPrintRequest.TdxValidator;

public sealed class SettingsException : Exception
{
    public SettingsException(string message) : base(message)
    {
    }
}

public sealed class TicketSettings
{
    public required TicketConfiguration Configuration { get; init; }

    public required LookupSchedule Schedule { get; init; }
}

public static class SettingsFile
{
    public const string FileName = "tdx-validator.settings.json";

    public static TicketSettings Load(string path)
    {
        var json = File.ReadAllText(path);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (JsonException ex)
        {
            throw new SettingsException("The settings file is not valid JSON. " + ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new SettingsException("The settings file must be a JSON object.");

            var attributes = ReadAttributes(root);
            var attempts = ReadRequiredInt(root, "lookupAttempts");
            var intervalSeconds = ReadRequiredInt(root, "lookupIntervalSeconds");
            if (attempts < 1)
                throw new SettingsException("lookupAttempts must be at least 1.");
            if (intervalSeconds < 1)
                throw new SettingsException("lookupIntervalSeconds must be at least 1.");

            return new TicketSettings
            {
                Configuration = new TicketConfiguration
                {
                    ApplicationId = ReadRequiredInt(root, "applicationId"),
                    FormId = ReadRequiredInt(root, "formId"),
                    TypeId = ReadRequiredInt(root, "typeId"),
                    StatusId = ReadRequiredInt(root, "statusId"),
                    PriorityId = ReadRequiredInt(root, "priorityId"),
                    AccountId = ReadRequiredInt(root, "accountId"),
                    SourceId = ReadOptionalInt(root, "sourceId"),
                    ResponsibleGroupId = ReadOptionalInt(root, "responsibleGroupId"),
                    CustomAttributes = attributes
                },
                Schedule = new LookupSchedule(attempts, TimeSpan.FromSeconds(intervalSeconds))
            };
        }
    }

    private static List<TicketAttribute> ReadAttributes(JsonElement root)
    {
        if (!root.TryGetProperty("customAttributes", out var value) || value.ValueKind == JsonValueKind.Null)
            return [];
        if (value.ValueKind != JsonValueKind.Array)
            throw new SettingsException("customAttributes is not a well-formed array.");

        var attributes = new List<TicketAttribute>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new SettingsException("A custom attribute is not a well-formed object.");

            var text = item.TryGetProperty("value", out var raw) && raw.ValueKind == JsonValueKind.String
                ? raw.GetString() ?? string.Empty
                : throw new SettingsException("A custom attribute value is missing.");
            attributes.Add(new TicketAttribute
            {
                Id = ReadRequiredInt(item, "id"),
                Value = text
            });
        }

        return attributes;
    }

    private static int ReadRequiredInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            throw new SettingsException(name + " is missing.");
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 0)
            throw new SettingsException(name + " is not a well-formed integer.");

        return number;
    }

    private static int? ReadOptionalInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 0)
            throw new SettingsException(name + " is not a well-formed integer.");

        return number;
    }
}
