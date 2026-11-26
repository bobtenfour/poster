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

    /// <summary>Contract section 12.2: in the owner's capture the printer reports Gray as Low and everything else OK.</summary>
    [Fact]
    public void The_real_printer_capture_reports_gray_low_and_the_printheads_ok()
    {
        var status = PrinterStatusDocument.Parse(Fixture("open_status.xml"));

        Assert.NotNull(status);
        Assert.Equal(["Gray"], status.Cartridges.Where(item => item.NeedsAttention).Select(item => item.Name));
        Assert.Equal("Low", status.Cartridges.Single(item => item.Name == "Gray").Status);
        Assert.All(status.Cartridges.Where(item => item.Name != "Gray"), item => Assert.Equal("OK", item.Status));
        Assert.Equal(["Gray-Photo black", "Matte black-Yellow", "Magenta-Cyan"], status.Printheads.Select(item => item.Name));
        Assert.All(status.Printheads, item => Assert.False(item.NeedsAttention));
    }

    [Fact]
    public void A_bare_status_id_of_zero_needs_no_attention_and_a_missing_status_never_fails_the_reading()
    {
        var bare = PrinterStatusDocument.Parse(Fixture("printer-status.xml"));
        var missing = PrinterStatusDocument.Parse(Fixture("printer-status.xml").Replace("<Status>0</Status>", "", StringComparison.Ordinal));

        Assert.NotNull(bare);
        Assert.All(bare.Cartridges, item => Assert.False(item.NeedsAttention));
        Assert.All(bare.Cartridges, item => Assert.Null(item.Status));
        Assert.NotNull(missing);
        Assert.Equal(6, missing.Cartridges.Count);
        Assert.All(missing.Cartridges, item => Assert.False(item.NeedsAttention));
    }

    [Theory]
    [InlineData("<Status><Id>8</Id><Description>Low</Description></Status>", true, "Low")]
    [InlineData("<Status><Id>0</Id><Description>OK</Description></Status>", false, "OK")]
    [InlineData("<Status><Description>Missing</Description></Status>", true, "Missing")]
    [InlineData("<Status><Description>ok</Description></Status>", false, "ok")]
    [InlineData("<Status>5</Status>", true, null)]
    public void A_cartridge_needs_attention_when_the_printer_reports_a_status_other_than_ok(string element, bool attention, string? description)
    {
        var document = Fixture("printer-status.xml").Replace(
            "<Cartridge Id=\"1\" Color=\"G\"><Status>0</Status>",
            "<Cartridge Id=\"1\" Color=\"G\">" + element,
            StringComparison.Ordinal);

        var gray = PrinterStatusDocument.Parse(document)!.Cartridges.Single(item => item.Name == "Gray");

        Assert.Equal(attention, gray.NeedsAttention);
        Assert.Equal(description, gray.Status);
    }

    [Fact]
    public void A_printhead_needs_attention_when_its_status_id_is_not_zero()
    {
        var fixture = Fixture("printer-status.xml");
        var start = fixture.IndexOf("<Status>", fixture.IndexOf("Color=\"C-M\"", StringComparison.Ordinal), StringComparison.Ordinal);
        var end = fixture.IndexOf("</Status>", start, StringComparison.Ordinal) + "</Status>".Length;
        var document = fixture[..start] + "<Status><Id>3</Id><Description>Replace</Description></Status>" + fixture[end..];

        var printheads = PrinterStatusDocument.Parse(document)!.Printheads;

        Assert.Equal([false, true, false], printheads.Select(item => item.NeedsAttention));
        Assert.Equal("Replace", printheads[1].Status);
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
