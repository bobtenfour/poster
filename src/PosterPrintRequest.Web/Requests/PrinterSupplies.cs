using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace PosterPrintRequest.Web.Requests;

public sealed record PrinterCartridge(string Name, int Percent);

public sealed class PrinterReading
{
    private PrinterReading(bool available, IReadOnlyList<PrinterCartridge> cartridges)
    {
        Available = available;
        Cartridges = cartridges;
    }

    public bool Available { get; }

    public IReadOnlyList<PrinterCartridge> Cartridges { get; }

    public static PrinterReading Unavailable { get; } = new(false, []);

    public static PrinterReading Create(IReadOnlyList<PrinterCartridge> cartridges)
    {
        ArgumentNullException.ThrowIfNull(cartridges);
        if (cartridges.Count != 6)
        {
            throw new ArgumentOutOfRangeException(nameof(cartridges));
        }

        var copy = new PrinterCartridge[6];
        for (var index = 0; index < 6; index++)
        {
            copy[index] = cartridges[index];
        }

        return new PrinterReading(true, copy);
    }
}

public sealed class PrinterSupplies : IDisposable
{
    public const string BaseUrlKey = "PrinterMonitoring:BaseUrl";

    public const string SuppliesPath = PrinterStatusDocument.RelativePath;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly IConfiguration _configuration;

    private readonly HttpClient _http;

    public PrinterSupplies(IConfiguration configuration, HttpClient http)
    {
        _configuration = configuration;
        _http = http;
    }

    public static string? Target(IConfiguration configuration) =>
        Target(configuration[BaseUrlKey]);

    public static string? Target(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        return baseUrl.TrimEnd('/') + SuppliesPath;
    }

    public static HttpClient CreateClient() =>
        CreateClient(new SocketsHttpHandler
        {
            // Printer Monitoring Contract v1: never follow redirects; anonymous request.
            AllowAutoRedirect = false,
            UseCookies = false,
            PreAuthenticate = false,
            Credentials = null,
            DefaultProxyCredentials = null
        });

    public static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler, disposeHandler: true)
        {
            Timeout = Timeout
        };

    public async Task<PrinterReading> ReadAsync(CancellationToken cancellationToken)
    {
        var html = await ReadBodyAsync(cancellationToken);
        if (html is null)
        {
            return PrinterReading.Unavailable;
        }

        var cartridges = Parse(html);
        return cartridges is null ? PrinterReading.Unavailable : PrinterReading.Create(cartridges);
    }

    private async Task<string?> ReadBodyAsync(CancellationToken cancellationToken)
    {
        var target = Target(_configuration);
        if (target is null)
        {
            return null;
        }

        // Printer Monitoring Contract v1: one request, no redirects (a 3xx is not a success),
        // at most 1 MiB of body, and one total time limit for the whole consultation.
        var totalLimit = _http.Timeout > TimeSpan.Zero && _http.Timeout < Timeout ? _http.Timeout : Timeout;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(totalLimit);
        try
        {
            using var response = await _http.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            if (!response.IsSuccessStatusCode
                || PrinterHttpBoundary.IsRedirect(response.StatusCode)
                || PrinterHttpBoundary.DeclaresTooMuch(response))
            {
                return null;
            }

            var bytes = await PrinterHttpBoundary.ReadLimitedAsync(response.Content, limit.Token);
            if (bytes is null)
            {
                return null;
            }

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
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Printer Monitoring Contract amendment of 25 November 2026: the six cartridges of open_status.xml.</summary>
    public static IReadOnlyList<PrinterCartridge>? Parse(string? document) => PrinterStatusDocument.Parse(document)?.Cartridges;

    public void Dispose() => _http.Dispose();
}
