using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

/// <summary>Printer Monitoring Contract amendment of 25 November 2026: open_status.xml replaces the HTML supplies page.</summary>
public sealed class PrinterStatusDocumentTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(
        RepositoryPaths.Root(), "tests", "PosterPrintRequest.Tests", "Fixtures", name));

    [Fact]
    public void Six_cartridges_use_remaining_ink_over_capacity_and_three_printheads_are_read()
    {
        var status = PrinterStatusDocument.Parse(Fixture("printer-status.xml"));

        Assert.NotNull(status);
        Assert.Equal(["Gray", "Photo black", "Matte black", "Yellow", "Magenta", "Cyan"], status.Cartridges.Select(item => item.Name));
        Assert.Equal([27, 36, 80, 34, 89, 70], status.Cartridges.Select(item => item.Percent));
        Assert.Equal(["MK-G", "C-M", "Y-PK"], status.Printheads.Select(item => item.Name));
        Assert.All(status.Printheads, item => Assert.Equal("OK", item.Status));
        Assert.Equal(["3", "3", "2"], status.Printheads.Select(item => item.Health));
    }

    /// <summary>
    /// The owner's sanitized capture of the real printer. It must be copied to
    /// tests/PosterPrintRequest.Tests/Fixtures/open_status.xml; acceptance requires this test to pass on it.
    /// </summary>
    [Fact]
    public void The_real_printer_capture_gives_six_cartridges_and_three_printheads()
    {
        var path = Path.Combine(RepositoryPaths.Root(), "tests", "PosterPrintRequest.Tests", "Fixtures", "open_status.xml");
        Assert.True(File.Exists(path), "Copy the sanitized open_status.xml of the T1300 to tests/PosterPrintRequest.Tests/Fixtures/open_status.xml.");

        var status = PrinterStatusDocument.Parse(File.ReadAllText(path));

        Assert.NotNull(status);
        Assert.Equal(6, status.Cartridges.Count);
        Assert.All(status.Cartridges, item => Assert.InRange(item.Percent, 0, 100));
        Assert.Equal(3, status.Printheads.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>Cartridges</body></html>")]
    [InlineData("<Serviceability><Printer_Status><Cartridges/></Printer_Status></Serviceability>")]
    [InlineData("<!DOCTYPE x [<!ENTITY e \"y\">]><Serviceability/>")]
    public void Anything_but_six_known_cartridges_is_not_a_reading(string document)
    {
        Assert.Null(PrinterStatusDocument.Parse(document));
    }

    [Fact]
    public void A_cartridge_with_zero_capacity_or_a_repeated_color_is_not_a_reading()
    {
        var zero = Fixture("printer-status.xml").Replace("<Capacity>130</Capacity><Ink_Consumed>91</Ink_Consumed>", "<Capacity>0</Capacity><Ink_Consumed>91</Ink_Consumed>", StringComparison.Ordinal);
        var repeated = Fixture("printer-status.xml").Replace("Color=\"C\"", "Color=\"M\"", StringComparison.Ordinal);

        Assert.Null(PrinterStatusDocument.Parse(zero));
        Assert.Null(PrinterStatusDocument.Parse(repeated));
    }
}
