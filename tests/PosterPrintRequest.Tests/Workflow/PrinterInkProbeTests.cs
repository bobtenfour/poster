using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

public sealed class PrinterInkProbeTests
{
    private const string PrinterIp = "192.0.2.10";

    private const string SuppliesUri = "http://192.0.2.10/hp/device/webAccess/index.htm?content=supplies";

    private static readonly string Fixture = File.ReadAllText(Path.Combine(
        RepositoryPaths.Root(),
        "tests",
        "PosterPrintRequest.Tests",
        "Fixtures",
        "printer-supplies.html"));

    [Fact]
    public void The_probe_client_is_anonymous()
    {
        using var handler = (HttpClientHandler)PrinterInkProbe.CreateAnonymousHandler();

        Assert.False(handler.UseDefaultCredentials);
        Assert.Null(handler.Credentials);
        Assert.False(handler.PreAuthenticate);
        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
        Assert.Null(handler.DefaultProxyCredentials);
    }

    [Fact]
    public async Task A_successful_anonymous_get_reports_six_cartridges_status_size_and_duration()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixture)
        }));

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.True(request.RequestUri!.IsAbsoluteUri);
        Assert.Equal(SuppliesUri, request.RequestUri.AbsoluteUri);
        Assert.Equal("/hp/device/webAccess/index.htm", request.RequestUri.AbsolutePath);
        Assert.Equal("?content=supplies", request.RequestUri.Query);
        Assert.Null(request.Headers.Authorization);
        Assert.DoesNotContain(request.Headers, header => header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Mozilla/5.0", Assert.Single(request.Headers.GetValues("User-Agent")));
        Assert.DoesNotContain(request.Headers, header => header.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(request.Headers, header => header.Key.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            ["Gray", "Photo black", "Matte black", "Yellow", "Magenta", "Cyan"],
            reading.Cartridges.Select(cartridge => cartridge.Name));
        Assert.Equal([27, 36, 80, 34, 89, 70], reading.Cartridges.Select(cartridge => cartridge.Percent));
        Assert.Equal("200", reading.HttpStatus);
        var bytes = Encoding.UTF8.GetByteCount(Fixture).ToString();
        Assert.Equal(bytes, reading.BytesRead);
        Assert.Equal(bytes, reading.ContentLength);
        Assert.Equal("text/plain; charset=utf-8", reading.ContentType);
        Assert.Equal("HTML", reading.Format);
        Assert.Equal("no", reading.Redirected);
        Assert.Equal("http://***/hp/device/webAccess/index.htm", reading.FinalUri);
        Assert.DoesNotContain(PrinterIp, reading.FinalUri);
        Assert.DoesNotContain("content=supplies", reading.FinalUri);
        Assert.Matches("^[0-9]+ ms$", reading.Duration);
        Assert.Equal("none", reading.Errors);
        Assert.DoesNotContain(PrinterIp, reading.Errors);
        Assert.DoesNotContain(SuppliesUri, reading.Errors);
    }

    [Fact]
    public async Task A_configured_ip_is_sent_as_an_absolute_anonymous_supplies_request()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixture)
        }));

        var reading = await PrinterInkProbe.ReadAsync(Configuration("  " + PrinterIp + "  "), CancellationToken.None, handler);

        var request = Assert.Single(handler.Requests);
        Assert.True(request.RequestUri!.IsAbsoluteUri);
        Assert.Equal(SuppliesUri, request.RequestUri.AbsoluteUri);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Null(request.Headers.Authorization);
        Assert.DoesNotContain(request.Headers, header => header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(PrinterIp, reading.Errors);
        Assert.DoesNotContain("http://", reading.Errors);
    }

    [Fact]
    public async Task An_absolute_base_without_a_path_still_requests_the_supplies_page()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixture)
        }));

        var reading = await PrinterInkProbe.ReadAsync(Configuration("http://" + PrinterIp + "/"), CancellationToken.None, handler);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(SuppliesUri, request.RequestUri!.AbsoluteUri);
        Assert.Equal("?content=supplies", request.RequestUri.Query);
        Assert.Equal("http://***/hp/device/webAccess/index.htm", reading.FinalUri);
        Assert.DoesNotContain("content=supplies", reading.FinalUri);
        Assert.DoesNotContain(PrinterIp, reading.Errors);
    }

    [Fact]
    public async Task A_configured_base_path_remains_on_the_supplies_request()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixture)
        }));

        var reading = await PrinterInkProbe.ReadAsync(
            Configuration("https://192.0.2.10/devices/plotter/"),
            CancellationToken.None,
            handler);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/devices/plotter/hp/device/webAccess/index.htm", request.RequestUri!.AbsolutePath);
        Assert.Equal("?content=supplies", request.RequestUri.Query);
        Assert.Equal(
            "https://192.0.2.10/devices/plotter/hp/device/webAccess/index.htm?content=supplies",
            request.RequestUri.AbsoluteUri);
        Assert.Equal("https://***/devices/plotter/hp/device/webAccess/index.htm", reading.FinalUri);
        Assert.DoesNotContain("content=supplies", reading.FinalUri);
        Assert.DoesNotContain(PrinterIp, reading.FinalUri);
        Assert.DoesNotContain(PrinterIp, reading.Errors);
    }

    [Fact]
    public async Task A_non_http_base_sends_no_request_and_does_not_echo_the_value()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("transport"));

        var reading = await PrinterInkProbe.ReadAsync(Configuration("ftp://192.0.2.10/devices"), CancellationToken.None, handler);

        Assert.Empty(handler.Requests);
        Assert.Equal("The supplies address is not an absolute URI.", reading.Errors);
        Assert.DoesNotContain(PrinterIp, reading.Errors);
        Assert.DoesNotContain("ftp://", reading.Errors);
    }

    [Theory]
    [InlineData("application/json", "{\"ok\":true}", "JSON")]
    [InlineData("text/xml", "<?xml version=\"1.0\"?><supplies />", "XML")]
    public async Task The_response_format_follows_the_content_type(string mediaType, string body, string format)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        }));

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        Assert.Equal(format, reading.Format);
        Assert.StartsWith(mediaType, reading.ContentType, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Encoding.UTF8.GetByteCount(body).ToString(), reading.BytesRead);
        Assert.DoesNotContain(PrinterIp, reading.FinalUri);
    }

    [Fact]
    public async Task A_failed_status_keeps_the_response_facts_and_no_cartridge_values()
    {
        const string body = "error page";
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(body)
        }));

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        Assert.Single(handler.Requests);
        Assert.Equal(SuppliesUri, handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Empty(reading.Cartridges);
        Assert.Equal("500", reading.HttpStatus);
        Assert.Equal(Encoding.UTF8.GetByteCount(body).ToString(), reading.BytesRead);
        Assert.Matches("^[0-9]+ ms$", reading.Duration);
        Assert.Contains("HTTP status 500.", reading.Errors);
        Assert.Contains("six cartridge readings", reading.Errors);
        Assert.DoesNotContain(PrinterIp, reading.FinalUri);
    }

    [Fact]
    public async Task A_transport_failure_reports_the_error_without_a_status()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("transport"));

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        Assert.Single(handler.Requests);
        Assert.Equal(SuppliesUri, handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Empty(reading.Cartridges);
        Assert.Equal("none", reading.HttpStatus);
        Assert.Equal("none", reading.BytesRead);
        Assert.Matches("^[0-9]+ ms$", reading.Duration);
        Assert.Contains("HttpRequestException: transport", reading.Errors);
        Assert.Equal("none", reading.FinalUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task A_blank_address_sends_no_request(string? baseUrl)
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("transport"));

        var reading = await PrinterInkProbe.ReadAsync(Configuration(baseUrl), CancellationToken.None, handler);

        Assert.Empty(handler.Requests);
        Assert.Empty(reading.Cartridges);
        Assert.Equal("none", reading.HttpStatus);
        Assert.Equal("none", reading.BytesRead);
        Assert.Equal("none", reading.FinalUri);
        Assert.Equal("Printer address is not configured.", reading.Errors);
        Assert.Equal("none", reading.BodyPreview);
        Assert.Equal("none", reading.PreviewTruncated);
    }

    [Fact]
    public async Task An_incomplete_supplies_page_produces_no_cartridge_readings()
    {
        const string html = "<html><head><title>HP Designjet T1300 - Supplies</title></head><body></body></html>";
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        }));

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/hp/device/webAccess/index.htm", request.RequestUri!.AbsolutePath);
        Assert.Equal("?content=supplies", request.RequestUri.Query);
        Assert.Empty(reading.Cartridges);
        Assert.Equal("200", reading.HttpStatus);
        Assert.Equal("HTML", reading.Format);
        Assert.Contains("six cartridge readings", reading.Errors);
        Assert.DoesNotContain("%", reading.Errors);
    }

    [Fact]
    public async Task The_preview_contains_the_actual_response_and_stays_within_1500_characters()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixture)
        }));

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        Assert.Contains("showTip('HP 72 Gray 27%')", reading.BodyPreview);
        Assert.Contains("<html", reading.BodyPreview);
        Assert.True(reading.BodyPreview.Length <= 1500);
        Assert.Equal("no", reading.PreviewTruncated);
        Assert.DoesNotContain(PrinterIp, reading.BodyPreview);
    }

    [Fact]
    public async Task A_long_response_preview_is_truncated_after_sanitization()
    {
        var secret = " http://192.0.2.10/admin?token=abc&password=secret printer.contoso.local CONTOSO\\print-svc Bearer eyJhbGci.secret.sig password=hunter2";
        var body = "<html><body>showTip('Gray 27%')" + secret + "\n" + new string('A', 1600) + "TAILMARKER</body></html>";
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        }));

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        Assert.Contains("showTip('Gray 27%')", reading.BodyPreview);
        Assert.Contains("<html", reading.BodyPreview);
        Assert.True(reading.BodyPreview.Length <= 1500);
        Assert.Equal(1500, reading.BodyPreview.Length);
        Assert.Equal("yes", reading.PreviewTruncated);
        Assert.DoesNotContain("TAILMARKER", reading.BodyPreview);
        Assert.DoesNotContain(PrinterIp, reading.BodyPreview);
        Assert.DoesNotContain("printer.contoso.local", reading.BodyPreview);
        Assert.DoesNotContain("eyJhbGci.secret.sig", reading.BodyPreview);
        Assert.DoesNotContain("hunter2", reading.BodyPreview);
        Assert.DoesNotContain("token=abc", reading.BodyPreview);
        Assert.DoesNotContain("password=secret", reading.BodyPreview);
        Assert.DoesNotContain("CONTOSO\\print-svc", reading.BodyPreview);
        Assert.DoesNotContain("print-svc", reading.BodyPreview);
    }

    [Fact]
    public async Task Sensitive_connection_details_are_removed_before_the_reading_is_returned()
    {
        var body = """
            <html>
            <body>
            showTip('Cyan 70%')
            http://192.0.2.10/hp/device/webAccess/index.htm?content=supplies&token=abc&password=secret
            printer.contoso.local
            CONTOSO\print-svc
            IIS APPPOOL\PosterApp
            Authorization: Bearer eyJhbGci.secret.sig
            Cookie: session=secret123
            password=hunter2
            localhost
            2001:db8::10
            svc-print@contoso.example
            </body>
            </html>
            """;
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/html")
        }));

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        Assert.Contains("showTip('Cyan 70%')", reading.BodyPreview);
        Assert.Contains("<html", reading.BodyPreview);
        Assert.Contains("index.htm", reading.BodyPreview);
        Assert.Equal("no", reading.PreviewTruncated);
        Assert.DoesNotContain(PrinterIp, reading.BodyPreview);
        Assert.DoesNotContain("printer.contoso.local", reading.BodyPreview);
        Assert.DoesNotContain("contoso", reading.BodyPreview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token=abc", reading.BodyPreview);
        Assert.DoesNotContain("password=secret", reading.BodyPreview);
        Assert.DoesNotContain("hunter2", reading.BodyPreview);
        Assert.DoesNotContain("eyJhbGci.secret.sig", reading.BodyPreview);
        Assert.DoesNotContain("secret123", reading.BodyPreview);
        Assert.DoesNotContain("CONTOSO\\print-svc", reading.BodyPreview);
        Assert.DoesNotContain("IIS APPPOOL\\PosterApp", reading.BodyPreview);
        Assert.DoesNotContain("print-svc", reading.BodyPreview);
        Assert.DoesNotContain("PosterApp", reading.BodyPreview);
        Assert.DoesNotContain("content=supplies", reading.BodyPreview);
        Assert.DoesNotContain("localhost", reading.BodyPreview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2001:db8::10", reading.BodyPreview);
        Assert.DoesNotContain("svc-print", reading.BodyPreview);
        Assert.DoesNotContain("contoso.example", reading.BodyPreview);
    }

    [Fact]
    public async Task An_empty_response_body_is_reported_explicitly()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty)
        }));

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        Assert.Equal("0", reading.BytesRead);
        Assert.Equal("Empty response", reading.BodyPreview);
        Assert.Equal("no", reading.PreviewTruncated);
        Assert.DoesNotContain(PrinterIp, reading.BodyPreview);
    }

    private static IConfiguration Configuration(string? baseUrl) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PrinterSupplies.BaseUrlKey] = baseUrl
            })
            .Build();

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
            _send = send;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return _send(request, cancellationToken);
        }
    }
}
