using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Configuration;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

public sealed class PrinterSuppliesTests
{
    private const string SuppliesPath = "/hp/device/webAccess/index.htm?content=supplies";

    private static readonly string ReferencePage = File.ReadAllText(Path.Combine(
        RepositoryPaths.Root(),
        "tests",
        "PosterPrintRequest.Tests",
        "Fixtures",
        "printer-supplies.html"));

    [Fact]
    public void Committed_configuration_leaves_the_printer_address_empty()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryPaths.WebProjectDirectory(), "appsettings.json"), optional: false)
            .Build();

        Assert.Equal(string.Empty, configuration[PrinterSupplies.BaseUrlKey]);
        Assert.Null(PrinterSupplies.Target(configuration));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void A_blank_base_address_has_no_request_target(string? baseUrl)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PrinterSupplies.BaseUrlKey] = baseUrl
            })
            .Build();

        Assert.Null(PrinterSupplies.Target(baseUrl));
        Assert.Null(PrinterSupplies.Target(configuration));
    }

    [Fact]
    public void A_missing_key_has_no_request_target()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        Assert.Null(PrinterSupplies.Target(configuration));
    }

    [Fact]
    public void The_request_target_keeps_a_non_root_path_and_trims_only_trailing_slashes()
    {
        const string baseUrl = "http://printer.example/plotter///";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PrinterSupplies.BaseUrlKey] = baseUrl
            })
            .Build();

        var expected = "http://printer.example/plotter" + SuppliesPath;
        Assert.Equal(expected, PrinterSupplies.Target(baseUrl));
        Assert.Equal(expected, PrinterSupplies.Target(configuration));
    }

    [Fact]
    public void The_request_target_is_exact_string_concatenation()
    {
        const string baseUrl = "http://printer.example/plotter?keep=1";

        Assert.Equal(baseUrl + SuppliesPath, PrinterSupplies.Target(baseUrl));
    }

    [Fact]
    public void The_environment_variable_resolves_through_the_same_configuration_key()
    {
        const string variable = "PrinterMonitoring__BaseUrl";
        const string baseUrl = "http://printer.example/plotter/";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, baseUrl);
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            Assert.Equal(baseUrl.TrimEnd('/') + SuppliesPath, PrinterSupplies.Target(configuration));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public void The_supplies_client_timeout_is_twenty_seconds()
    {
        using var client = PrinterSupplies.CreateClient();

        Assert.Equal(TimeSpan.FromSeconds(20), client.Timeout);
        Assert.Null(client.BaseAddress);
    }

    [Fact]
    public async Task One_get_uses_the_exact_slice_1_address_and_returns_the_body()
    {
        const string baseUrl = "http://printer.example/plotter///";
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReferencePage)
        }));
        using var client = PrinterSupplies.CreateClient(handler);
        using var supplies = new PrinterSupplies(Configuration(baseUrl), client);

        var result = await supplies.ReadAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(20), client.Timeout);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(PrinterSupplies.Target(baseUrl), request.RequestUri!.OriginalString);
        Assert.True(result.Available);
        Assert.Equal(PrinterSupplies.Parse(ReferencePage), result.Cartridges);
    }

    [Fact]
    public async Task A_non_success_status_is_a_failure()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("error page")
        }));
        using var supplies = new PrinterSupplies(Configuration("http://printer.example/plotter"), PrinterSupplies.CreateClient(handler));

        var result = await supplies.ReadAsync(CancellationToken.None);

        Assert.False(result.Available);
        Assert.Empty(result.Cartridges);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_transport_failure_returns_no_body()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("transport"));
        using var supplies = new PrinterSupplies(Configuration("http://printer.example/plotter"), PrinterSupplies.CreateClient(handler));
        PrinterReading? result = null;

        var exception = await Record.ExceptionAsync(async () => result = await supplies.ReadAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result.Available);
        Assert.Empty(result.Cartridges);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_client_timeout_returns_no_body()
    {
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(200) };
        using var supplies = new PrinterSupplies(Configuration("http://printer.example/plotter"), client);
        var started = Stopwatch.StartNew();
        PrinterReading? result = null;

        var exception = await Record.ExceptionAsync(async () => result = await supplies.ReadAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result.Available);
        Assert.Empty(result.Cartridges);
        Assert.Single(handler.Requests);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(20));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task A_blank_address_sends_no_request(string? baseUrl)
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("transport"));
        using var supplies = new PrinterSupplies(Configuration(baseUrl), PrinterSupplies.CreateClient(handler));

        var result = await supplies.ReadAsync(CancellationToken.None);

        Assert.False(result.Available);
        Assert.Empty(result.Cartridges);
        Assert.Empty(handler.Requests);
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
