using System.Text;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

public sealed class PrinterSuppliesParserTests
{
    private static readonly string Fixture = File.ReadAllText(Path.Combine(
        RepositoryPaths.Root(),
        "tests",
        "PosterPrintRequest.Tests",
        "Fixtures",
        "printer-supplies.html"));

    private static readonly PrinterCartridge[] Expected =
    [
        new("Gray", 27),
        new("Photo black", 36),
        new("Matte black", 80),
        new("Yellow", 34),
        new("Magenta", 89),
        new("Cyan", 70)
    ];

    private static readonly string[] Headers =
    [
        "Code",
        "Ink cartridge",
        "Status",
        "Ink level",
        "Capacity",
        "Warranty status",
        "HP Order Information"
    ];

    [Fact]
    public void The_real_supplies_page_supplies_six_canonical_readings()
    {
        var html = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root(),
            "tests",
            "PosterPrintRequest.Tests",
            "Fixtures",
            "printer-supplies-real.html"));

        Assert.Contains("<caption>Cartridges</caption>", html, StringComparison.Ordinal);
        Assert.Contains("<th scope=\"col\">&nbsp;</th>", html, StringComparison.Ordinal);
        Assert.Contains("Ink level&#42;", html, StringComparison.Ordinal);
        Assert.Contains("border-bottom: 79px solid #000000;", html, StringComparison.Ordinal);
        Assert.Contains("<td nowrap=\"nowrap\">80%</td>", html, StringComparison.Ordinal);
        Assert.Contains("jsessionid=redacted", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\b(?:10|127|169|172|192)(?:\.\d{1,3}){3}\b", html);

        var cartridges = PrinterSupplies.Parse(html);

        Assert.Equal(Expected, cartridges);
        Assert.Equal(80, Assert.Single(cartridges!, cartridge => cartridge.Name == "Matte black").Percent);
    }

    [Fact]
    public void The_captured_table_supplies_six_canonical_readings()
    {
        Assert.Contains("<caption>Cartridges</caption>", Fixture, StringComparison.Ordinal);
        Assert.Contains(">Y</td><td>HP 72 Yellow</td><td>Out of warranty</td><td>34%</td>", Fixture, StringComparison.Ordinal);

        var cartridges = PrinterSupplies.Parse(Fixture);

        Assert.Equal(Expected, cartridges);
    }

    [Fact]
    public void ShowTip_text_is_not_a_cartridge_source()
    {
        var scriptStart = Fixture.IndexOf("<script", StringComparison.OrdinalIgnoreCase);
        var scriptEnd = Fixture.IndexOf("</script>", scriptStart, StringComparison.OrdinalIgnoreCase);
        var withoutScript = Fixture.Remove(scriptStart, scriptEnd + "</script>".Length - scriptStart);
        var contradicted = Fixture.Replace(
            "showTip('HP 72 Gray 27%')",
            "showTip('HP 72 Gray 1%')",
            StringComparison.Ordinal);
        var tableStart = Fixture.IndexOf("<table", StringComparison.OrdinalIgnoreCase);
        var tableEnd = Fixture.IndexOf("</table>", tableStart, StringComparison.OrdinalIgnoreCase);
        var scriptOnly = Fixture.Remove(tableStart, tableEnd + "</table>".Length - tableStart);

        Assert.Equal(Expected, PrinterSupplies.Parse(withoutScript));
        Assert.Equal(Expected, PrinterSupplies.Parse(contradicted));
        Assert.Null(PrinterSupplies.Parse(scriptOnly));
        Assert.Null(PrinterSupplies.Parse("showTip('HP 72 Gray 27%');showTip('HP 72 Photo black 36%');"));
    }

    [Fact]
    public void The_cartridges_caption_selects_the_table()
    {
        var decoy = CapturedRows();
        foreach (var row in decoy)
        {
            row[3] = "1%";
        }

        var html = "<html><body>"
            + Table("Printheads", Headers, decoy)
            + Table("Cartridges", Headers, CapturedRows())
            + "</body></html>";

        Assert.Equal(Expected, PrinterSupplies.Parse(html));
        Assert.Null(PrinterSupplies.Parse(Table("cartridges", Headers, CapturedRows())));
        Assert.Null(PrinterSupplies.Parse(Table("Supplies", Headers, CapturedRows())));
        Assert.Null(PrinterSupplies.Parse(Page(Headers, CapturedRows()) + Page(Headers, CapturedRows())));
    }

    [Fact]
    public void Columns_come_from_the_header_row_rather_than_cell_position()
    {
        var headers = new[]
        {
            "Slot",
            "Ink level",
            "Code",
            "HP Order Information",
            "Capacity",
            "Warranty status",
            "Status",
            "Ink cartridge"
        };
        var rows = CapturedRows();
        rows[3][5] = "99%";
        var reordered = rows.Select(row => new[]
        {
            "extra",
            row[3],
            row[0],
            row[6],
            row[4],
            row[5],
            row[2],
            row[1]
        }).ToArray();

        Assert.Equal(Expected, PrinterSupplies.Parse(Page(headers, reordered)));
    }

    [Fact]
    public void Out_of_warranty_does_not_reject_the_yellow_reading()
    {
        var rows = CapturedRows();
        rows[3] = ["Y", "HP 72 Yellow", "Out of warranty", "34%", "n/a", "99%", "n/a"];

        var cartridges = PrinterSupplies.Parse(Page(Headers, rows));

        Assert.Equal(34, Assert.Single(cartridges!, cartridge => cartridge.Name == "Yellow").Percent);
    }

    [Fact]
    public void Warranty_text_in_the_ink_level_column_is_rejected()
    {
        var rows = CapturedRows();
        rows[3] = ["Y", "HP 72 Yellow", "OK", "Out of warranty", "n/a", "34%", "n/a"];

        Assert.Null(PrinterSupplies.Parse(Page(Headers, rows)));
    }

    [Fact]
    public void Row_order_does_not_change_the_canonical_result()
    {
        var rows = CapturedRows();
        rows.Reverse();

        Assert.Equal(Expected, PrinterSupplies.Parse(Page(Headers, rows)));
    }

    [Fact]
    public void A_missing_cartridge_row_is_rejected()
    {
        var rows = CapturedRows();
        rows.RemoveAt(5);

        Assert.Null(PrinterSupplies.Parse(Page(Headers, rows)));
    }

    [Fact]
    public void A_duplicate_cartridge_code_is_rejected()
    {
        var rows = CapturedRows();
        rows[5] = ["G", "HP 72 Gray", "OK", "27%", "n/a", "In warranty", "n/a"];

        Assert.Null(PrinterSupplies.Parse(Page(Headers, rows)));
    }

    [Fact]
    public void Conflicting_duplicate_readings_are_rejected()
    {
        var rows = CapturedRows();
        rows[5] = ["G", "HP 72 Gray", "OK", "10%", "n/a", "In warranty", "n/a"];

        Assert.Null(PrinterSupplies.Parse(Page(Headers, rows)));
    }

    [Theory]
    [InlineData(0, 1, "HP 72 Magenta")]
    [InlineData(1, 1, "HP 72 Matte black")]
    [InlineData(1, 1, "HP 72 Photo Black")]
    [InlineData(2, 1, "black")]
    [InlineData(4, 1, "HP 72 Cyan")]
    public void A_mismatched_code_and_name_are_rejected(int row, int column, string value)
    {
        Assert.Null(PrinterSupplies.Parse(With(row, column, value)));
    }

    [Theory]
    [InlineData("101%")]
    [InlineData("-1%")]
    [InlineData("00%")]
    [InlineData("1000%")]
    [InlineData("27")]
    [InlineData("27 %")]
    [InlineData("")]
    [InlineData("27%%")]
    [InlineData("%")]
    [InlineData("010%")]
    [InlineData("1.5%")]
    [InlineData("27&nbsp;%")]
    public void A_malformed_ink_level_is_rejected(string level)
    {
        Assert.Null(PrinterSupplies.Parse(With(0, 3, level)));
    }

    [Theory]
    [InlineData("BL")]
    [InlineData("g")]
    [InlineData("P")]
    [InlineData("PKK")]
    [InlineData("Gray")]
    public void An_unknown_cartridge_code_is_rejected(string code)
    {
        Assert.Null(PrinterSupplies.Parse(With(0, 0, code)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("<html></html>")]
    [InlineData("<html><head><title>HP Designjet T1300 - Supplies</title></head><body></body></html>")]
    [InlineData("<caption>Cartridges</caption>")]
    public void A_missing_cartridges_table_is_rejected(string? html)
    {
        Assert.Null(PrinterSupplies.Parse(html));
    }

    [Fact]
    public void An_incomplete_or_malformed_cartridges_table_is_rejected()
    {
        var withoutCapacity = Headers.Where(header => header != "Capacity").ToArray();
        var rows = CapturedRows().Select(row => row.Where((_, index) => index != 4).ToArray()).ToArray();
        var duplicateHeader = Headers.ToArray();
        duplicateHeader[6] = "Ink level";
        var unclosed = "<table><caption>Cartridges</caption><tr><th>Code</th></tr>";
        var unclosedGauge = Page(Headers, CapturedRows()).Replace(
            "<td>27%</td>",
            "<td><table><tr><td>27%</td></tr></td>",
            StringComparison.Ordinal);
        var scriptTable = "<script>" + Page(Headers, CapturedRows()) + "</script>";

        Assert.Null(PrinterSupplies.Parse(Page(Headers, CapturedRows()).Replace("<caption>Cartridges</caption>", "", StringComparison.Ordinal)));
        Assert.Null(PrinterSupplies.Parse(Page(withoutCapacity, rows)));
        Assert.Null(PrinterSupplies.Parse(Page(duplicateHeader, CapturedRows())));
        Assert.Null(PrinterSupplies.Parse(unclosedGauge));
        Assert.Null(PrinterSupplies.Parse(unclosed));
        Assert.Null(PrinterSupplies.Parse(scriptTable));
        Assert.Null(PrinterSupplies.Parse(With(3, 2, "")));
    }

    [Fact]
    public void A_gauge_table_inside_the_ink_cell_does_not_reject_the_reading()
    {
        var html = Page(Headers, CapturedRows())
            .Replace(
                "<table class=\"data\">",
                "<table class=\"layout\"><tr><td><table class=\"data\">",
                StringComparison.Ordinal)
            .Replace("</table>", "</table></td></tr></table>", StringComparison.Ordinal)
            .Replace(
                "<caption>Cartridges</caption><tr>",
                "<caption>Cartridges</caption><tr><td colspan=\"7\">Cartridges</td></tr><tr>",
                StringComparison.Ordinal)
            .Replace(
                "<td>27%</td>",
                "<td onmouseover=\"showTip('HP 72 Gray 1%')\"><table><tr><td width=\"27%\">&nbsp;</td><td width=\"73%\">&nbsp;</td></tr></table>27%</td>",
                StringComparison.Ordinal)
            .Replace("<td>OK</td>", "<td><img alt=\"OK\"> OK</td>", StringComparison.Ordinal);

        Assert.Equal(Expected, PrinterSupplies.Parse(html));
    }

    [Fact]
    public void An_image_alt_percent_is_the_ink_level_when_the_cell_has_no_text()
    {
        var html = Page(Headers, CapturedRows())
            .Replace("<td>36%</td>", "<td><img alt=\"36%\"></td>", StringComparison.Ordinal)
            .Replace("<td>80%</td>", "<td><img alt=\"10%\">80%</td>", StringComparison.Ordinal);

        Assert.Equal(Expected, PrinterSupplies.Parse(html.Replace("<td><img alt=\"10%\">80%</td>", "<td>80%</td>", StringComparison.Ordinal)));
        Assert.Null(PrinterSupplies.Parse(html));
    }

    [Fact]
    public void Markup_around_the_cells_does_not_change_the_readings()
    {
        var html = Page(Headers, CapturedRows())
            .Replace("<table class=\"data\">", "<TABLE class=\"data\">", StringComparison.Ordinal)
            .Replace("</table>", "</TABLE>", StringComparison.Ordinal)
            .Replace("<caption>Cartridges</caption>", "<caption>\nCartridges\n</caption>", StringComparison.Ordinal)
            .Replace("<td>27%</td>", "<td class=\"level\"><span>27%</span></td>", StringComparison.Ordinal);

        Assert.Equal(Expected, PrinterSupplies.Parse(html));
    }

    private static string With(int row, int column, string value)
    {
        var rows = CapturedRows();
        rows[row][column] = value;
        return Page(Headers, rows);
    }

    private static string Page(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows) =>
        "<html><body>" + Table("Cartridges", headers, rows) + "</body></html>";

    private static string Table(string caption, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.Append("<table class=\"data\"><caption>").Append(caption).Append("</caption><tr>");
        foreach (var header in headers)
        {
            builder.Append("<th>").Append(header).Append("</th>");
        }

        builder.Append("</tr>");
        foreach (var row in rows)
        {
            builder.Append("<tr>");
            foreach (var cell in row)
            {
                builder.Append("<td>").Append(cell).Append("</td>");
            }

            builder.Append("</tr>");
        }

        builder.Append("</table>");
        return builder.ToString();
    }

    private static List<string[]> CapturedRows() =>
    [
        ["G", "HP 72 Gray", "OK", "27%", "n/a", "In warranty", "n/a"],
        ["PK", "HP 72 Photo black", "OK", "36%", "n/a", "In warranty", "n/a"],
        ["MK", "HP 72 Matte black", "OK", "80%", "n/a", "In warranty", "n/a"],
        ["Y", "HP 72 Yellow", "Out of warranty", "34%", "n/a", "In warranty", "n/a"],
        ["M", "HP 72 Magenta", "OK", "89%", "n/a", "In warranty", "n/a"],
        ["C", "HP 72 Cyan", "OK", "70%", "n/a", "In warranty", "n/a"]
    ];
}
