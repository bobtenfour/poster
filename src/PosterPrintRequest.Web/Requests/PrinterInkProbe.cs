using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace PosterPrintRequest.Web.Requests;

public sealed class PrinterInkProbe
{
    private const string BaseUrlKey = "PrinterMonitoring:BaseUrl";

    private const string SuppliesPath = "/hp/device/webAccess/index.htm";

    private const string SuppliesQuery = "content=supplies";

    private const string SuppliesUserAgent = "Mozilla/5.0";

    private const int MaxRedirects = 50;

    public sealed record Reading(
        IReadOnlyList<PrinterCartridge> Cartridges,
        string HttpStatus,
        string ContentType,
        string ContentLength,
        string BytesRead,
        string Duration,
        string Redirected,
        string RedirectCount,
        string FinalUri,
        string Format,
        string BodyPreview,
        string PreviewTruncated,
        string Errors)
    {
        public static Reading Empty { get; } = new(
            [],
            "none",
            "none",
            "none",
            "none",
            "0 ms",
            "none",
            "none",
            "none",
            "none",
            "none",
            "none",
            "none");
    }

    public static HttpMessageHandler CreateAnonymousHandler() =>
        new HttpClientHandler
        {
            AllowAutoRedirect = false,
            PreAuthenticate = false,
            UseCookies = false,
            DefaultProxyCredentials = null,
            Credentials = null,
            UseDefaultCredentials = false
        };

    public static Task<Reading> ReadAsync(IConfiguration? configuration, CancellationToken cancellationToken) =>
        ReadAsync(configuration, cancellationToken, null);

    public static async Task<Reading> ReadAsync(
        IConfiguration? configuration,
        CancellationToken cancellationToken,
        HttpMessageHandler? handler)
    {
        var started = Stopwatch.StartNew();
        var ownsHandler = handler is null;
        handler ??= CreateAnonymousHandler();
        using var client = new HttpClient(handler, disposeHandler: ownsHandler)
        {
            Timeout = PrinterSupplies.Timeout
        };

        Uri? suppliesUri = null;
        try
        {
            if (!TryCreateSuppliesUri(configuration, out var created, out var targetError) || created is null)
            {
                return Finish(started, [], "none", "none", "none", "none", "none", "none", "none", "none", "none", "none", targetError);
            }

            suppliesUri = created;
            if (!created.IsAbsoluteUri)
            {
                return Finish(started, [], "none", "none", "none", "none", "none", "none", "none", "none", "none", "none", "The supplies address is not an absolute URI.");
            }

            var (response, redirects, finalUri) = await SendAsync(client, created, cancellationToken);
            using (response)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
                var contentType = response.Content.Headers.ContentType?.ToString();
                var contentLength = response.Content.Headers.ContentLength?.ToString(CultureInfo.InvariantCulture);
                var decoded = Decode(response, bytes);
                var cartridges = PrinterSupplies.Parse(decoded);
                var (preview, truncated) = CreatePreview(decoded);
                var errors = new List<string>();
                if (redirects >= MaxRedirects && IsRedirect(response.StatusCode))
                {
                    errors.Add("Redirect limit reached.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    errors.Add("HTTP status " + status + ".");
                }

                if (cartridges is null)
                {
                    errors.Add("The supplies response did not contain six cartridge readings.");
                }

                return Finish(
                    started,
                    cartridges ?? [],
                    status,
                    string.IsNullOrWhiteSpace(contentType) ? "none" : contentType,
                    contentLength ?? "none",
                    bytes.LongLength.ToString(CultureInfo.InvariantCulture),
                    redirects > 0 ? "yes" : "no",
                    redirects.ToString(CultureInfo.InvariantCulture),
                    Sanitize(finalUri),
                    DetectFormat(response.Content.Headers.ContentType?.MediaType, bytes),
                    preview,
                    truncated,
                    errors.Count == 0 ? "none" : string.Join(" ", errors));
            }
        }
        catch (Exception exception)
        {
            return Finish(
                started,
                [],
                "none",
                "none",
                "none",
                "none",
                "none",
                "none",
                "none",
                "none",
                "none",
                "none",
                Describe(exception, suppliesUri));
        }
    }

    private static async Task<(HttpResponseMessage Response, int Redirects, Uri FinalUri)> SendAsync(
        HttpClient client,
        Uri start,
        CancellationToken cancellationToken)
    {
        var current = start;
        var redirects = 0;
        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("User-Agent", SuppliesUserAgent);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!IsRedirect(response.StatusCode) || response.Headers.Location is null || redirects >= MaxRedirects)
            {
                return (response, redirects, current);
            }

            var location = response.Headers.Location;
            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            response.Dispose();
            current = next;
            redirects++;
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static string Sanitize(Uri uri)
    {
        var path = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
        return uri.Scheme + "://***" + path;
    }

    private static string DetectFormat(string? mediaType, byte[] bytes)
    {
        var media = mediaType ?? "";
        if (media.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return "HTML";
        }

        if (media.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return "JSON";
        }

        if (media.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            return "XML";
        }

        var sampleLength = Math.Min(bytes.Length, 256);
        var sample = Encoding.UTF8.GetString(bytes, 0, sampleLength).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (sample.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
            || sample.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return "HTML";
        }

        if (sample.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            return "XML";
        }

        if (sample.StartsWith('{') || sample.StartsWith('['))
        {
            return "JSON";
        }

        return "Unrecognized";
    }

    private static bool TryCreateSuppliesUri(IConfiguration? configuration, out Uri? uri, out string error)
    {
        uri = null;
        var configured = configuration?[BaseUrlKey]?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            error = "Printer address is not configured.";
            return false;
        }

        var suppliesTarget = SuppliesPath + "?" + SuppliesQuery;
        string target;
        if (IPAddress.TryParse(configured, out var address))
        {
            var host = address.AddressFamily == AddressFamily.InterNetworkV6
                ? "[" + address.ToString() + "]"
                : address.ToString();
            target = Uri.UriSchemeHttp + "://" + host + suppliesTarget;
        }
        else
        {
            target = configured.TrimEnd('/') + suppliesTarget;
        }

        if (!Uri.TryCreate(target, UriKind.Absolute, out var candidate)
            || (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || !string.IsNullOrEmpty(candidate.Fragment)
            || !candidate.AbsolutePath.EndsWith(SuppliesPath, StringComparison.Ordinal)
            || candidate.Query != "?" + SuppliesQuery)
        {
            error = "The supplies address is not an absolute URI.";
            return false;
        }

        uri = candidate;
        error = "";
        return true;
    }

    private const int PreviewLimit = 1500;

    private const string EmptyPreview = "Empty response";

    private const string WithheldPreview = "Preview withheld because sanitization could not remove sensitive information.";

    private static readonly Regex UrlPattern = new(
        @"\b[a-zA-Z][a-zA-Z0-9+.-]*://[^\s""'<>]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex Ipv4Pattern = new(
        @"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex Ipv6Pattern = new(
        @"(?<![A-Za-z0-9])(?:[A-Fa-f0-9]{0,4}:){2,7}[A-Fa-f0-9]{0,4}(?![A-Za-z0-9:])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex EmailPattern = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex LocalHostPattern = new(
        @"\blocalhost\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex HostNamePattern = new(
        @"\b(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+(?:[A-Za-z]{2,63})\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex SensitiveAssignmentPattern = new(
        @"\b(password|passwd|pwd|token|secret|api_key|apikey|access_token|authorization|cookie|credential|session)(\s*[:=]\s*)(?:""[^""]*""|'[^']*'|\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex BearerPattern = new(
        @"\bBearer\s+\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex BasicPattern = new(
        @"\bBasic\s+[A-Za-z0-9+/=]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex HeaderSecretPattern = new(
        @"\b(Authorization|Cookie)\s*:\s*[^\r\n]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex WindowsAccountPattern = new(
        @"\b(?:IIS APPPOOL|NT AUTHORITY|[A-Za-z][A-Za-z0-9._-]{0,63})\\[A-Za-z][A-Za-z0-9._$-]{0,63}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly string[] FileExtensions =
    [
        "htm", "html", "xml", "js", "css", "png", "jpg", "jpeg", "gif", "svg", "json", "txt", "ico"
    ];

    private static (string Preview, string Truncated) CreatePreview(string body)
    {
        if (body.Length == 0)
        {
            return (EmptyPreview, "no");
        }

        string sanitized;
        try
        {
            sanitized = SanitizeBody(body);
        }
        catch (RegexMatchTimeoutException)
        {
            return (WithheldPreview, "no");
        }

        if (!IsSafe(sanitized))
        {
            return (WithheldPreview, "no");
        }

        var truncated = sanitized.Length > PreviewLimit;
        var preview = truncated ? sanitized[..PreviewLimit] : sanitized;
        if (!IsSafe(preview))
        {
            return (WithheldPreview, "no");
        }

        return (preview, truncated ? "yes" : "no");
    }

    private static string SanitizeBody(string body)
    {
        var value = UrlPattern.Replace(body, match => RedactUrl(match.Value));
        value = HeaderSecretPattern.Replace(value, "$1: ***");
        value = BearerPattern.Replace(value, "Bearer ***");
        value = BasicPattern.Replace(value, "Basic ***");
        value = SensitiveAssignmentPattern.Replace(value, "$1$2***");
        value = Ipv4Pattern.Replace(value, "***");
        value = Ipv6Pattern.Replace(value, "***");
        value = WindowsAccountPattern.Replace(value, "***");
        value = EmailPattern.Replace(value, "***");
        value = LocalHostPattern.Replace(value, "***");
        value = RedactHostNames(value);
        return value;
    }

    private static string RedactUrl(string value)
    {
        var trimmed = value.TrimEnd('.', ',', ';', ')');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return "http://***";
        }

        var path = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
        return uri.Scheme + "://***" + path;
    }

    private static bool IsSafe(string value)
    {
        try
        {
            if (Ipv4Pattern.IsMatch(value)
                || Ipv6Pattern.IsMatch(value)
                || WindowsAccountPattern.IsMatch(value)
                || EmailPattern.IsMatch(value)
                || LocalHostPattern.IsMatch(value))
            {
                return false;
            }

            if (UrlPattern.IsMatch(value) && UnsafeUrl(value))
            {
                return false;
            }

            if (SensitiveAssignmentPattern.IsMatch(value) && UnsafeAssignment(value))
            {
                return false;
            }

            if (BearerPattern.IsMatch(value) && UnsafeBearer(value))
            {
                return false;
            }

            if (HeaderSecretPattern.IsMatch(value) && UnsafeHeader(value))
            {
                return false;
            }

            if (!string.Equals(value, RedactHostNames(value), StringComparison.Ordinal))
            {
                return false;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }

        return true;
    }

    private static bool UnsafeUrl(string value)
    {
        foreach (Match match in UrlPattern.Matches(value))
        {
            if (!match.Value.Contains("://***", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UnsafeAssignment(string value)
    {
        foreach (Match match in SensitiveAssignmentPattern.Matches(value))
        {
            if (!match.Value.EndsWith("***", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UnsafeBearer(string value)
    {
        foreach (Match match in BearerPattern.Matches(value))
        {
            if (!string.Equals(match.Value, "Bearer ***", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UnsafeHeader(string value)
    {
        foreach (Match match in HeaderSecretPattern.Matches(value))
        {
            if (!match.Value.EndsWith("***", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string RedactHostNames(string value) =>
        HostNamePattern.Replace(value, match =>
        {
            var last = match.Value.Split('.')[^1];
            var inPath = match.Index > 0 && value[match.Index - 1] == '/';
            if (inPath)
            {
                foreach (var extension in FileExtensions)
                {
                    if (string.Equals(last, extension, StringComparison.OrdinalIgnoreCase))
                    {
                        return match.Value;
                    }
                }
            }

            return "***";
        });

    private static string Describe(Exception exception, Uri? suppliesUri)
    {
        var message = exception.Message;
        if (suppliesUri is not null
            && (message.Contains(suppliesUri.Host, StringComparison.OrdinalIgnoreCase)
                || message.Contains(suppliesUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
                || message.Contains("://", StringComparison.Ordinal)))
        {
            return exception.GetType().Name;
        }

        return exception.GetType().Name + ": " + message;
    }

    private static Reading Finish(
        Stopwatch started,
        IReadOnlyList<PrinterCartridge> cartridges,
        string status,
        string contentType,
        string contentLength,
        string bytesRead,
        string redirected,
        string redirectCount,
        string finalUri,
        string format,
        string bodyPreview,
        string previewTruncated,
        string errors)
    {
        var duration = started.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms";
        return new Reading(
            cartridges,
            status,
            contentType,
            contentLength,
            bytesRead,
            duration,
            redirected,
            redirectCount,
            finalUri,
            format,
            bodyPreview,
            previewTruncated,
            errors);
    }

    private static string Decode(HttpResponseMessage response, byte[] bytes)
    {
        var charset = response.Content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim().Trim('"')).GetString(bytes);
            }
            catch (ArgumentException)
            {
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
