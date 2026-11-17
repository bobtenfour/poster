using System.Globalization;
using Microsoft.Extensions.Configuration;
using PosterPrintRequest.Tdx;

namespace PosterPrintRequest.Web.Tdx;

/// <summary>
/// TDX configuration (TDX Integration Contract section 9.1). Every value comes from configuration; nothing is
/// written in code or in the repository. Missing or invalid values disable ticket creation (section 9.4).
/// </summary>
public sealed class TdxOptions
{
    public const string Section = "Tdx";

    public required Uri BaseUrl { get; init; }

    public required TicketConfiguration Ticket { get; init; }

    public required LookupSchedule Lookups { get; init; }

    public required string CredentialFile { get; init; }

    /// <summary>Fixed timeout of each HTTP call (section 8A.3).</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Reads the options. Returns null and a reason without values when the configuration is incomplete or invalid.
    /// </summary>
    public static TdxOptions? Read(IConfiguration configuration, out string? problem)
    {
        var section = configuration.GetSection(Section);
        problem = null;
        if (!section.Exists())
        {
            problem = "The TDX configuration is not present.";
            return null;
        }

        var missing = new List<string>();
        var baseText = section["BaseUrl"];
        Uri? baseUrl = null;
        if (string.IsNullOrWhiteSpace(baseText) || !Uri.TryCreate(baseText.Trim(), UriKind.Absolute, out baseUrl))
        {
            missing.Add("BaseUrl");
        }
        else
        {
            try
            {
                baseUrl = TdxAddresses.ValidateBaseAddress(baseUrl);
            }
            catch (TdxAddressException)
            {
                missing.Add("BaseUrl");
            }
        }

        int Required(string key)
        {
            if (int.TryParse(section[key], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0)
            {
                return value;
            }

            missing.Add(key);
            return 0;
        }

        int? Optional(string key)
        {
            var text = section[key];
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0)
            {
                return value;
            }

            missing.Add(key);
            return null;
        }

        int Bounded(string key, int fallback, int min, int max)
        {
            var text = section[key];
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max)
            {
                return value;
            }

            missing.Add(key);
            return fallback;
        }

        var ticket = new TicketConfiguration
        {
            ApplicationId = Required("ApplicationId"),
            FormId = Required("FormId"),
            TypeId = Required("TypeId"),
            StatusId = Required("StatusId"),
            PriorityId = Required("PriorityId"),
            AccountId = Required("AccountId"),
            ResponsibleGroupId = Required("ResponsibleGroupId"),
            SourceId = Optional("SourceId")
        };
        var attempts = Bounded("LookupAttempts", 3, 1, 10);
        var interval = Bounded("LookupIntervalSeconds", 10, 1, 120);
        var credential = section["CredentialFile"];
        if (string.IsNullOrWhiteSpace(credential))
        {
            missing.Add("CredentialFile");
        }

        if (missing.Count > 0)
        {
            problem = "The TDX configuration is incomplete or invalid: " + string.Join(", ", missing) + ".";
            return null;
        }

        return new TdxOptions
        {
            BaseUrl = baseUrl!,
            Ticket = ticket,
            Lookups = new LookupSchedule(attempts, TimeSpan.FromSeconds(interval)),
            CredentialFile = credential!.Trim()
        };
    }
}
