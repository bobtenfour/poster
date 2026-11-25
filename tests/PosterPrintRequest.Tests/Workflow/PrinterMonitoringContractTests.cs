using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

/// <summary>
/// Printer Monitoring Contract v1, section 9: tests T1 to T7 and T9. T8 is in PrinterDashboardTests.
/// Every test uses an in-memory handler; no printer is contacted.
/// </summary>
public sealed class PrinterMonitoringContractTests
{
    private const string PrinterIp = "192.0.2.10";

    private const string SuppliesUri = "http://192.0.2.10/hp/device/webAccess/index.htm?content=supplies";

    private const string ForeignUri = "http://198.51.100.20/unintended";

    private static readonly string Fixture = File.ReadAllText(Path.Combine(
        RepositoryPaths.Root(),
        "tests",
        "PosterPrintRequest.Tests",
        "Fixtures",
        "printer-supplies.html"));

    // T1
    [Fact]
    public async Task T1_a_redirect_to_the_same_host_and_path_is_refused_without_a_second_request()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri(SuppliesUri);
            return Task.FromResult(redirect);
        });

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        Assert.Single(handler.Requests);
        Assert.Equal("Redirect refused (HTTP 302).", reading.Errors);
        Assert.Equal("refused", reading.Redirected);
        Assert.Equal("302", reading.HttpStatus);
        Assert.Empty(reading.Cartridges);
        Assert.Equal("http://***/hp/device/webAccess/index.htm", reading.FinalUri);
        Assert.DoesNotContain(PrinterIp, reading.Errors);
    }

    // T2
    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.SeeOther)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task T2_a_redirect_to_another_host_is_never_requested(HttpStatusCode status)
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var redirect = new HttpResponseMessage(status);
            redirect.Headers.Location = new Uri(ForeignUri);
            return Task.FromResult(redirect);
        });

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(SuppliesUri, request.RequestUri!.AbsoluteUri);
        Assert.DoesNotContain(handler.Requests, item => item.RequestUri!.Host == "198.51.100.20");
        Assert.Equal("Redirect refused (HTTP " + (int)status + ").", reading.Errors);
        Assert.Equal("refused", reading.Redirected);
        Assert.Empty(reading.Cartridges);
        Assert.DoesNotContain("198.51.100.20", reading.FinalUri);
        Assert.DoesNotContain("unintended", reading.FinalUri);
        Assert.DoesNotContain("198.51.100.20", reading.Errors);
    }

    // T3
    [Theory]
    [InlineData(HttpStatusCode.Ambiguous)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.NotModified)]
    public async Task T3_a_3xx_without_location_is_refused_without_a_second_request(HttpStatusCode status)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        Assert.Single(handler.Requests);
        Assert.Equal("Redirect refused (HTTP " + (int)status + ").", reading.Errors);
        Assert.Empty(reading.Cartridges);
    }

    // T4
    [Fact]
    public async Task T4_a_declared_length_above_one_mebibyte_is_not_read()
    {
        var body = new CountingStream(PrinterHttpBoundary.MaxResponseBytes + 1, endless: false);
        var handler = new RecordingHandler((_, _) =>
        {
            var content = new StreamContent(body);
            content.Headers.ContentLength = PrinterHttpBoundary.MaxResponseBytes + 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        Assert.Single(handler.Requests);
        Assert.Equal(0, body.Served);
        Assert.Equal("The printer response is larger than 1 MiB.", reading.Errors);
        Assert.Equal("0", reading.BytesRead);
        Assert.Empty(reading.Cartridges);
    }

    // T5
    [Fact]
    public async Task T5_a_body_without_length_stops_once_it_passes_one_mebibyte()
    {
        var body = new CountingStream(0, endless: true);
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        Assert.Equal("none", reading.ContentLength);
        Assert.Equal("The printer response is larger than 1 MiB.", reading.Errors);
        Assert.Empty(reading.Cartridges);
        Assert.InRange(body.Served, PrinterHttpBoundary.MaxResponseBytes + 1, PrinterHttpBoundary.MaxResponseBytes + (64 * 1024));
    }

    // T6
    [Fact]
    public async Task T6_a_valid_page_of_exactly_one_mebibyte_is_read_and_parsed()
    {
        var page = Padded(Fixture, PrinterHttpBoundary.MaxResponseBytes);
        Assert.Equal(PrinterHttpBoundary.MaxResponseBytes, page.Length);
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(page) }));

        var reading = await PrinterInkProbe.ReadAsync(Configuration(PrinterIp), CancellationToken.None, handler);

        Assert.Equal("none", reading.Errors);
        Assert.Equal("no", reading.Redirected);
        Assert.Equal(PrinterHttpBoundary.MaxResponseBytes.ToString(), reading.BytesRead);
        Assert.Equal([27, 36, 80, 34, 89, 70], reading.Cartridges.Select(cartridge => cartridge.Percent));
    }

    // T7
    [Fact]
    public async Task T7_a_body_slower_than_the_total_limit_stops_the_consultation()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) }));
        var watch = Stopwatch.StartNew();

        var reading = await PrinterInkProbe.ReadAsync(
            Configuration(PrinterIp),
            CancellationToken.None,
            handler,
            TimeSpan.FromMilliseconds(200));

        watch.Stop();
        Assert.Single(handler.Requests);
        Assert.Equal("The printer did not answer within 20 seconds.", reading.Errors);
        Assert.Empty(reading.Cartridges);
        Assert.True(watch.Elapsed < PrinterSupplies.Timeout, "The total limit did not stop the body read.");
    }

    [Fact]
    public async Task T7_headers_slower_than_the_total_limit_stop_the_consultation()
    {
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var reading = await PrinterInkProbe.ReadAsync(
            Configuration(PrinterIp),
            CancellationToken.None,
            handler,
            TimeSpan.FromMilliseconds(200));

        Assert.Equal("The printer did not answer within 20 seconds.", reading.Errors);
        Assert.Empty(reading.Cartridges);
    }

    // T9
    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    public async Task T9_printer_supplies_refuses_a_redirect_without_a_second_request(HttpStatusCode status)
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var redirect = new HttpResponseMessage(status);
            redirect.Headers.Location = new Uri(ForeignUri);
            return Task.FromResult(redirect);
        });
        using var supplies = new PrinterSupplies(Configuration("http://" + PrinterIp), PrinterSupplies.CreateClient(handler));

        var reading = await supplies.ReadAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.False(reading.Available);
    }

    [Fact]
    public async Task T9_printer_supplies_does_not_read_a_declared_length_above_one_mebibyte()
    {
        var body = new CountingStream(PrinterHttpBoundary.MaxResponseBytes + 1, endless: false);
        var handler = new RecordingHandler((_, _) =>
        {
            var content = new StreamContent(body);
            content.Headers.ContentLength = PrinterHttpBoundary.MaxResponseBytes + 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var supplies = new PrinterSupplies(Configuration("http://" + PrinterIp), PrinterSupplies.CreateClient(handler));

        var reading = await supplies.ReadAsync(CancellationToken.None);

        Assert.False(reading.Available);
        Assert.Equal(0, body.Served);
    }

    [Fact]
    public async Task T9_printer_supplies_stops_a_body_without_length_after_one_mebibyte()
    {
        var body = new CountingStream(0, endless: true);
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
        using var supplies = new PrinterSupplies(Configuration("http://" + PrinterIp), PrinterSupplies.CreateClient(handler));

        var reading = await supplies.ReadAsync(CancellationToken.None);

        Assert.False(reading.Available);
        Assert.InRange(body.Served, PrinterHttpBoundary.MaxResponseBytes + 1, PrinterHttpBoundary.MaxResponseBytes + (64 * 1024));
    }

    [Fact]
    public async Task T9_printer_supplies_reads_a_valid_page_of_exactly_one_mebibyte()
    {
        var page = Padded(Fixture, PrinterHttpBoundary.MaxResponseBytes);
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(page) }));
        using var supplies = new PrinterSupplies(Configuration("http://" + PrinterIp), PrinterSupplies.CreateClient(handler));

        var reading = await supplies.ReadAsync(CancellationToken.None);

        Assert.True(reading.Available);
        Assert.Equal([27, 36, 80, 34, 89, 70], reading.Cartridges.Select(cartridge => cartridge.Percent));
    }

    [Fact]
    public async Task T9_printer_supplies_stops_a_slow_body_at_the_total_limit()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) }));
        using var client = PrinterSupplies.CreateClient(handler);
        client.Timeout = TimeSpan.FromMilliseconds(200);
        using var supplies = new PrinterSupplies(Configuration("http://" + PrinterIp), client);
        var watch = Stopwatch.StartNew();

        var reading = await supplies.ReadAsync(CancellationToken.None);

        watch.Stop();
        Assert.False(reading.Available);
        Assert.True(watch.Elapsed < PrinterSupplies.Timeout, "The total limit did not stop the body read.");
    }

    private static byte[] Padded(string html, int size)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        var page = new byte[size];
        Array.Fill(page, (byte)' ');
        Array.Copy(bytes, page, bytes.Length);
        return page;
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

    /// <summary>Non-seekable stream of 'x' bytes that counts what it served.</summary>
    private sealed class CountingStream(long length, bool endless) : Stream
    {
        public long Served { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Served; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var left = endless ? count : (int)Math.Min(count, length - Served);
            if (left <= 0)
            {
                return 0;
            }

            Array.Fill(buffer, (byte)'x', offset, left);
            Served += left;
            return left;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Stream whose reads never complete until cancelled.</summary>
    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
