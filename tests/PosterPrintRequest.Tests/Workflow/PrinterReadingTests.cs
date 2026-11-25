using System.Net;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

public sealed class PrinterReadingTests
{
    private static readonly string Fixture = File.ReadAllText(Path.Combine(
        RepositoryPaths.Root(),
        "tests",
        "PosterPrintRequest.Tests",
        "Fixtures",
        "printer-status.xml"));

    [Fact]
    public void An_available_reading_keeps_the_six_original_cartridges()
    {
        var cartridges = PrinterSupplies.Parse(Fixture);
        Assert.NotNull(cartridges);
        var reading = PrinterReading.Create(cartridges);

        Assert.True(reading.Available);
        Assert.Equal(cartridges, reading.Cartridges);
        Assert.Equal(
            ["Gray", "Photo black", "Matte black", "Yellow", "Magenta", "Cyan"],
            reading.Cartridges.Select(cartridge => cartridge.Name));
        Assert.Equal([27, 36, 80, 34, 89, 70], reading.Cartridges.Select(cartridge => cartridge.Percent));
    }

    [Fact]
    public void An_unavailable_reading_has_no_cartridges()
    {
        Assert.False(PrinterReading.Unavailable.Available);
        Assert.Empty(PrinterReading.Unavailable.Cartridges);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(7)]
    public void A_partial_collection_cannot_become_available(int count)
    {
        var cartridges = Enumerable.Range(0, count)
            .Select(index => new PrinterCartridge("Cartridge " + index.ToString(), index))
            .ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() => PrinterReading.Create(cartridges));
    }

    [Fact]
    public void The_reading_type_has_no_public_constructor()
    {
        Assert.Empty(typeof(PrinterReading).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public async Task A_successful_read_is_replaced_by_an_empty_failure()
    {
        var calls = 0;
        var handler = new SequenceHandler((_, _) =>
        {
            calls++;
            if (calls == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Fixture)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("supplies page")
            });
        });
        using var supplies = new PrinterSupplies(Configuration(), PrinterSupplies.CreateClient(handler));

        var first = await supplies.ReadAsync(CancellationToken.None);
        var second = await supplies.ReadAsync(CancellationToken.None);

        Assert.True(first.Available);
        Assert.Equal(6, first.Cartridges.Count);
        Assert.False(second.Available);
        Assert.Empty(second.Cartridges);
        Assert.Equal(2, calls);
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PrinterSupplies.BaseUrlKey] = "http://printer.example/plotter"
            })
            .Build();

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public SequenceHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
            _send = send;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _send(request, cancellationToken);
    }
}
