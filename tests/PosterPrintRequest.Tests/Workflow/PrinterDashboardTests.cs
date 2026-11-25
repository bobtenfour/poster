using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Components.Pages;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

public sealed class PrinterDashboardTests : BunitContext
{
    private static readonly string Fixture = File.ReadAllText(Path.Combine(
        RepositoryPaths.Root(),
        "tests",
        "PosterPrintRequest.Tests",
        "Fixtures",
        "printer-supplies.html"));

    public PrinterDashboardTests()
    {
        Services.AddSingleton<ITechnicianDashboard>(new IdleDashboard());
        Services.AddSingleton<IPrintingInventory>(new IdleInventory());
        Services.AddSingleton<ITechnicianWorkflow>(new IdleWorkflow());
        Services.AddSingleton<ITechnicianSearch>(new IdleSearch());
    }

    [Fact]
    public void The_retained_card_is_the_only_printer_card_and_the_dashboard_stays_in_place()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixture)
        }));
        Services.AddSingleton(Supplies("http://printer.example/plotter/", handler));

        var cut = Render<Technician>();

        Assert.Empty(handler.Requests);
        var card = Assert.Single(cut.FindAll(".epx-ink"));
        Assert.Equal("printer-ink-process", card.Id);
        Assert.Equal("Printer cartridges", cut.Find("#printer-ink-process-title").TextContent);
        Assert.DoesNotContain("printer-ink-title", cut.Markup);
        Assert.DoesNotContain("Printer cartridges in-process", cut.Markup);
        Assert.Contains("id=\"printer-ink-unavailable\">Printer unavailable", cut.Markup);
        AssertNoDiagnostics(cut.Markup);
        Assert.Contains("id=\"stat-pending\">4", cut.Markup);
        Assert.Contains(">Needs Reorder<", cut.Markup);
    }

    [Fact]
    public void A_failed_original_supplies_client_does_not_add_a_printer_card()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        Services.AddSingleton(Supplies("http://printer.example/plotter", handler));

        var cut = Render<Technician>();

        Assert.Empty(handler.Requests);
        Assert.Single(cut.FindAll(".epx-ink"));
        Assert.DoesNotContain("printer-ink-title", cut.Markup);
        Assert.Contains("id=\"printer-ink-unavailable\">Printer unavailable", cut.Markup);
        AssertNoDiagnostics(cut.Markup);
        Assert.DoesNotContain("27%", cut.Markup);
        Assert.DoesNotContain("Photo black", cut.Markup);
        Assert.Contains("id=\"stat-pending\">4", cut.Markup);
        Assert.Contains(">Needs Reorder<", cut.Markup);
    }

    [Fact]
    public void The_in_process_card_reads_independently_and_leaves_the_original_request_alone()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Fixture)
        }));
        Services.AddSingleton(Supplies("http://printer.example/plotter/", handler));

        var cut = Render<Technician>();

        Assert.Empty(handler.Requests);
        Assert.Single(cut.FindAll(".epx-ink"));
        Assert.Equal("Printer cartridges", cut.Find("#printer-ink-process-title").TextContent);
        Assert.DoesNotContain("printer-ink-title", cut.Markup);
        Assert.DoesNotContain("Printer cartridges in-process", cut.Markup);
        Assert.Contains("id=\"printer-ink-unavailable\">Printer unavailable", cut.Markup);
        AssertNoDiagnostics(cut.Markup);
        Assert.DoesNotContain("Process identity", cut.Markup);
        Assert.DoesNotContain("27%", cut.Markup);
        Assert.DoesNotContain("Photo black", cut.Markup);
        Assert.Contains("id=\"stat-pending\">4", cut.Markup);
    }

    [Fact]
    public async Task The_experimental_card_renders_a_sanitized_preview_as_inert_text()
    {
        const string printerIp = "192.0.2.10";
        var body = """
            <html>
            <body>
            showTip('Gray 27%')
            http://192.0.2.10/hp/device/webAccess/index.htm?token=abc&password=secret
            printer.contoso.local
            CONTOSO\print-svc
            </body>
            </html>
            """;
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/html")
        }));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PrinterSupplies.BaseUrlKey] = printerIp
            })
            .Build();
        var reading = await PrinterInkProbe.ReadAsync(configuration, CancellationToken.None, handler);

        Assert.DoesNotContain(printerIp, reading.BodyPreview);
        Assert.DoesNotContain("printer.contoso.local", reading.BodyPreview);
        Assert.DoesNotContain("token=abc", reading.BodyPreview);
        Assert.DoesNotContain("CONTOSO\\print-svc", reading.BodyPreview);
        Assert.Contains("<html", reading.BodyPreview);

        Services.AddSingleton(Supplies("http://printer.example/plotter/", new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture) }))));
        var cut = Render<Technician>();
        typeof(Technician).GetProperty("InkProbe", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(cut.Instance, reading);
        cut.Render();

        var cartridges = PrinterSupplies.Parse(Fixture);
        Assert.NotNull(cartridges);
        typeof(Technician).GetProperty("InkProbe", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(cut.Instance, reading with { Cartridges = cartridges });
        cut.Render();

        Assert.Empty(cut.FindAll("#printer-ink-process-preview"));
        Assert.DoesNotContain("printer-ink-unavailable", cut.Markup);
        AssertNoDiagnostics(cut.Markup);
        Assert.DoesNotContain("showTip", cut.Markup);
        Assert.DoesNotContain("<html", cut.Markup);
        Assert.DoesNotContain("&lt;html", cut.Markup);
        Assert.DoesNotContain(printerIp, cut.Markup);
        Assert.DoesNotContain("printer.contoso.local", cut.Markup);
        Assert.DoesNotContain("token=abc", cut.Markup);
        Assert.DoesNotContain("CONTOSO\\print-svc", cut.Markup);
        Assert.DoesNotContain("Process identity", cut.Markup);
        AssertCartridge(cut, "Gray", 27);
        AssertCartridge(cut, "Photo black", 36);
        AssertCartridge(cut, "Matte black", 80);
        AssertCartridge(cut, "Yellow", 34);
        AssertCartridge(cut, "Magenta", 89);
        AssertCartridge(cut, "Cyan", 70);
        Assert.NotEmpty(cut.FindAll("[data-ink='Photo black'] .epx-ink-sheen"));
        Assert.Empty(cut.FindAll("[data-ink='Matte black'] .epx-ink-sheen"));
    }

    [Fact]
    public void A_blank_printer_address_shows_printer_unavailable_without_a_request()
    {
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("transport"));
        Services.AddSingleton(Supplies(" ", handler));

        var cut = Render<Technician>();

        Assert.Empty(handler.Requests);
        Assert.Single(cut.FindAll(".epx-ink"));
        Assert.Equal("Printer cartridges", cut.Find("#printer-ink-process-title").TextContent);
        Assert.DoesNotContain("printer-ink-title", cut.Markup);
        Assert.Contains("id=\"printer-ink-unavailable\">Printer unavailable", cut.Markup);
        AssertNoDiagnostics(cut.Markup);
        Assert.DoesNotContain("27%", cut.Markup);
        Assert.Contains("id=\"stat-pending\">4", cut.Markup);
    }

    // Printer Monitoring Contract v1, T8: the dashboard renders while the printer fails in each way.
    [Theory]
    [InlineData("redirect")]
    [InlineData("too-large")]
    [InlineData("slow")]
    [InlineData("http-error")]
    [InlineData("transport")]
    [InlineData("unparseable")]
    public async Task T8_the_dashboard_renders_with_the_printer_unavailable_when_the_printer_fails(string failure)
    {
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            switch (failure)
            {
                case "redirect":
                    var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                    redirect.Headers.Location = new Uri("http://198.51.100.20/unintended");
                    return redirect;
                case "too-large":
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[PrinterHttpBoundary.MaxResponseBytes + 1])
                    };
                case "slow":
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                case "http-error":
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("error") };
                case "transport":
                    throw new HttpRequestException("transport");
                default:
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>no table</html>") };
            }
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PrinterSupplies.BaseUrlKey] = "192.0.2.10"
            })
            .Build();
        var reading = await PrinterInkProbe.ReadAsync(configuration, CancellationToken.None, handler, TimeSpan.FromMilliseconds(200));
        Assert.Empty(reading.Cartridges);
        Assert.NotEqual("none", reading.Errors);
        Assert.Single(handler.Requests);

        Services.AddSingleton(Supplies("http://printer.example/plotter/", new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture) }))));
        var cut = Render<Technician>();
        typeof(Technician).GetProperty("InkProbe", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(cut.Instance, reading);
        cut.Render();

        Assert.Contains("id=\"printer-ink-unavailable\">Printer unavailable", cut.Markup);
        Assert.Contains("id=\"stat-pending\">4", cut.Markup);
        AssertNoDiagnostics(cut.Markup);
        Assert.DoesNotContain("198.51.100.20", cut.Markup);
        Assert.DoesNotContain("192.0.2.10", cut.Markup);
    }

    private static void AssertNoDiagnostics(string markup)
    {
        Assert.DoesNotContain("HTTP status", markup);
        Assert.DoesNotContain("Content-Type", markup);
        Assert.DoesNotContain("Content-Length", markup);
        Assert.DoesNotContain("Bytes read", markup);
        Assert.DoesNotContain("Duration:", markup);
        Assert.DoesNotContain("Redirect:", markup);
        Assert.DoesNotContain("Redirects:", markup);
        Assert.DoesNotContain("Final URI", markup);
        Assert.DoesNotContain("Format:", markup);
        Assert.DoesNotContain("Preview truncated", markup);
        Assert.DoesNotContain("printer-ink-process-preview", markup);
        Assert.DoesNotContain("printer-ink-process-errors", markup);
        Assert.DoesNotContain("Errors:", markup);
    }

    private static void AssertCartridge(IRenderedComponent<Technician> cut, string name, int percent)
    {
        var cartridge = cut.Find($"[data-ink='{name}']");
        Assert.Equal(percent.ToString(CultureInfo.InvariantCulture), cartridge.GetAttribute("data-level"));
        Assert.Contains(percent.ToString(CultureInfo.InvariantCulture) + "%", cartridge.TextContent);
        var height = double.Parse(
            cartridge.QuerySelector(".epx-ink-level")!.GetAttribute("height")!,
            CultureInfo.InvariantCulture);
        Assert.Equal(58d * percent / 100d, height, 2);
    }

    private static PrinterSupplies Supplies(string? baseUrl, HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PrinterSupplies.BaseUrlKey] = baseUrl
            })
            .Build();
        return new PrinterSupplies(configuration, PrinterSupplies.CreateClient(handler));
    }

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

    private sealed class IdleDashboard : ITechnicianDashboard
    {
        public Task<TechnicianDashboardSnapshot> LoadAsync(int year, int month, CancellationToken cancellationToken) =>
            Task.FromResult(new TechnicianDashboardSnapshot
            {
                Year = year,
                Month = month,
                PeriodLabel = "October 2026",
                PendingPrinting = 4,
                PrintedThisMonth = 1,
                PrintedThisYear = 2,
                AwaitingPickup = 3,
                TotalProcessed = 5,
                Performance = [],
                Queue = []
            });
    }

    private sealed class IdleInventory : IPrintingInventory
    {
        public Task<ConsumableAttention> CriticalAlertsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ConsumableAttention
            {
                Depleted = [],
                LowStock = [],
                ExpiringSoon = [],
                NeedsReorder = new SupplyCategorySummary { ByType = [] },
                ExpiringSoonSummary = new SupplyCategorySummary { ByType = [] }
            });

        public Task<PrintingInventoryView> LoadAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<InventoryOutcome> AddStockAsync(int printingConsumableId, int quantity, DateOnly? expirationDate, InstitutionalActor? actor, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<InventoryOutcome> RemoveStockAsync(
            int printingConsumableId,
            int quantity,
            bool earliestExpirationAcknowledged,
            DateOnly? acknowledgedExpirationDate,
            InstitutionalActor? actor,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ConsumableLots?> LoadLotsAsync(int printingConsumableId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<InventoryOutcome> RemoveFromLotAsync(int printingConsumableId, DateOnly? expirationDate, int quantity, bool outOfOrderConfirmed, InstitutionalActor? actor, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<InventoryOutcome> AdjustLotAsync(int printingConsumableId, DateOnly? expirationDate, int quantityChange, string? reason, InstitutionalActor? actor, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    // No search runs on these renders: the dashboard is opened without search parameters.
    private sealed class IdleSearch : ITechnicianSearch
    {
        public Task<PosterSearchResult> ByNameAsync(string text, CancellationToken cancellationToken) => Task.FromResult(Empty());

        public Task<PosterSearchResult> ByDateAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken) => Task.FromResult(Empty());

        public Task<PosterSearchResult> ByEventAsync(int reasonId, CancellationToken cancellationToken) => Task.FromResult(Empty());

        public Task<IReadOnlyList<PosterSearchEvent>> EventsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PosterSearchEvent>>([]);

        private static PosterSearchResult Empty() => new() { Rows = [], More = false };
    }

    private sealed class IdleWorkflow : ITechnicianWorkflow
    {
        public Task<TechnicianView?> FindAsync(string? posterId, CancellationToken cancellationToken) =>
            Task.FromResult<TechnicianView?>(null);

        public Task<TechnicianOutcome> TakeAsync(string posterId, InstitutionalActor? actor, DateOnly? received, string? comments, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TechnicianOutcome> MarkPrintedAsync(string posterId, string? comments, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TechnicianOutcome> MarkLaminatedAsync(string posterId, string? comments, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TechnicianOutcome> MarkNotifiedAsync(string posterId, string? comments, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TechnicianOutcome> CompletePickupAsync(string posterId, DateOnly? dateOut, string? pickedUpBy, string? comments, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
