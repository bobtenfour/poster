using System.Text.RegularExpressions;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Tests.Hosting;
using PosterPrintRequest.Web.Components.Pages;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

/// <summary>Operator Configuration Presentation Contract v1: landing, windows, menu and printer compatibility.</summary>
public sealed class OperatorConfigurationWindowsSourceTests
{
    private static string Page(string name) =>
        File.ReadAllText(Path.Combine(RepositoryPaths.Root(), "src", "PosterPrintRequest.Web", "Components", "Pages", name));

    // Section 4.
    [Fact]
    public void The_landing_is_static_and_shows_three_equal_cards_in_order()
    {
        var landing = Page("TechnicianConfiguration.razor");

        Assert.Contains("@page \"/technician/configuration\"", landing, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", landing, StringComparison.Ordinal);
        Assert.DoesNotContain("<SessionGuard", landing, StringComparison.Ordinal);
        Assert.Contains("PosterAccess.OperatorPolicy", landing, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(RepositoryPaths.Root(), "src", "PosterPrintRequest.Web", "Components", "Pages", "TechnicianConfiguration.razor.cs")));

        var cards = Regex.Matches(landing, "<a id=\"(config-card-[a-z]+)\" class=\"([^\"]+)\" href=\"@ApplicationUris.BrowserPath\\(Navigation, \"([^\"]+)\"\\)\">");
        Assert.Equal(["config-card-departments", "config-card-printers", "config-card-consumables"], cards.Select(card => card.Groups[1].Value));
        Assert.All(cards, card => Assert.Equal("epx-config-card", card.Groups[2].Value));
        Assert.Equal(
            ["/technician/configuration/departments", "/technician/configuration/printers", "/technician/configuration/consumables"],
            cards.Select(card => card.Groups[3].Value));
        Assert.True(landing.IndexOf(">Departments<", StringComparison.Ordinal) < landing.IndexOf(">Printers<", StringComparison.Ordinal));
        Assert.True(landing.IndexOf(">Printers<", StringComparison.Ordinal) < landing.IndexOf(">Consumables &amp; Materials<", StringComparison.Ordinal));
    }

    [Fact]
    public void The_cards_share_one_style_rule_in_one_row_from_768_px()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryPaths.Root(), "src", "PosterPrintRequest.Web", "wwwroot", "css", "design-system", "foundation.css"));

        Assert.Equal(1, Regex.Count(css, @"(?m)^\.epx-config-card \{"));
        Assert.DoesNotContain("#config-card-", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(3, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Matches(@"@media \(min-width: 768px\) \{\s*\.epx-config-cards \{\s*grid-template-columns: repeat\(3, minmax\(0, 1fr\)\);", css);
    }

    // Sections 5 to 7: each window holds exactly its two panels, is interactive and carries SessionGuard.
    [Theory]
    [InlineData("ConfigurationDepartments.razor", "/technician/configuration/departments", "departments", "events")]
    [InlineData("ConfigurationPrinters.razor", "/technician/configuration/printers", "printer-models", "printer-compatibility")]
    [InlineData("ConfigurationConsumables.razor", "/technician/configuration/consumables", "consumable-types", "consumables")]
    public void Each_window_holds_exactly_its_two_panels(string file, string route, string first, string second)
    {
        var page = Page(file);

        Assert.Contains("@page \"" + route + "\"", page, StringComparison.Ordinal);
        Assert.Contains("@rendermode InteractiveServer", page, StringComparison.Ordinal);
        Assert.Contains("<SessionGuard />", page, StringComparison.Ordinal);
        Assert.Contains("PosterAccess.OperatorPolicy", page, StringComparison.Ordinal);
        Assert.Contains("@inherits ConfigurationWindow", page, StringComparison.Ordinal);
        Assert.Contains("Kicker=\"Configuration\"", page, StringComparison.Ordinal);
        Assert.Contains("\"/technician/configuration\")\">Back to Configuration</a>", page, StringComparison.Ordinal);
        var panels = Regex.Matches(page, "<Panel Id=\"([a-z-]+)\"").Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal([first, second], panels);
    }

    // Section 7.1 and 7.2: compatibility left the consumable row; the summary stays.
    [Fact]
    public void The_consumable_row_keeps_only_the_compatibility_summary()
    {
        var consumables = Page("ConfigurationConsumables.razor");

        Assert.DoesNotContain("CompatibilityCheckId", consumables, StringComparison.Ordinal);
        Assert.DoesNotContain("TogglePrinter", consumables, StringComparison.Ordinal);
        Assert.Contains("@CompatibilitySummary(consumable)", consumables, StringComparison.Ordinal);
        Assert.Contains("Thresholds are edited on its row. Printer compatibility is edited in Printers.", consumables, StringComparison.Ordinal);
        Assert.DoesNotContain("Thresholds and printer compatibility are edited on its row.", consumables, StringComparison.Ordinal);
    }

    // Section 8.1: one Configuration entry, active on the landing and on the windows.
    [Fact]
    public void The_menu_keeps_one_configuration_entry()
    {
        var layout = File.ReadAllText(Path.Combine(RepositoryPaths.Root(), "src", "PosterPrintRequest.Web", "Components", "Layout", "TechnicianLayout.razor"));

        Assert.Equal(1, Regex.Count(layout, "Href=\"/technician/configuration"));
        Assert.Contains("ExceptPrefix3=\"/technician/configuration\"", layout, StringComparison.Ordinal);
        Assert.Contains("<ShellNavLink Href=\"/technician/configuration\">Configuration</ShellNavLink>", layout, StringComparison.Ordinal);
    }
}

/// <summary>Section 6: printer compatibility edited per printer model, with the Consumables Inventory Contract 4.3 rule.</summary>
[Collection("Workflow database")]
public sealed class PrinterCompatibilityWindowTests : BunitContext
{
    private readonly WorkflowDatabaseFixture _database;

    public PrinterCompatibilityWindowTests(WorkflowDatabaseFixture database)
    {
        _database = database;
        AddAuthorization().SetAuthorized("usero");
    }

    [Fact]
    public async Task Compatibility_is_linked_and_unlinked_from_the_printers_window()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var printerName = "Compat plotter " + suffix;
        var usesName = "Compat uses " + suffix;
        var noneName = "Compat none " + suffix;
        int printerId;
        int usesId;
        int noneId;
        int inkId;
        int linkedPadId;
        int otherPadId;
        await using (var setup = _database.CreateContext())
        {
            var configuration = new OperatorConfiguration(setup, NullLogger<OperatorConfiguration>.Instance);
            Assert.True((await configuration.AddPrinterModelAsync(printerName, CancellationToken.None)).Completed);
            Assert.True((await configuration.AddConsumableTypeAsync(usesName, ConsumableStockModes.Undated, false, true, 90, CancellationToken.None)).Completed);
            Assert.True((await configuration.AddConsumableTypeAsync(noneName, ConsumableStockModes.Undated, false, true, 91, CancellationToken.None)).Completed);
            printerId = (await setup.PrinterModels.SingleAsync(model => model.Name == printerName)).PrinterModelId;
            usesId = (await setup.ConsumableTypes.SingleAsync(type => type.Name == usesName)).ConsumableTypeId;
            noneId = (await setup.ConsumableTypes.SingleAsync(type => type.Name == noneName)).ConsumableTypeId;
            Assert.True((await configuration.AddConsumableAsync(usesId, "Ink " + suffix, null, null, CancellationToken.None)).Completed);
            Assert.True((await configuration.AddConsumableAsync(noneId, "Linked pad " + suffix, null, null, CancellationToken.None)).Completed);
            Assert.True((await configuration.AddConsumableAsync(noneId, "Other pad " + suffix, null, null, CancellationToken.None)).Completed);
            inkId = (await setup.PrintingConsumables.SingleAsync(item => item.Name == "Ink " + suffix)).PrintingConsumableId;
            linkedPadId = (await setup.PrintingConsumables.SingleAsync(item => item.Name == "Linked pad " + suffix)).PrintingConsumableId;
            otherPadId = (await setup.PrintingConsumables.SingleAsync(item => item.Name == "Other pad " + suffix)).PrintingConsumableId;
            Assert.True((await configuration.SetCompatibilityAsync(printerId, linkedPadId, true, CancellationToken.None)).Completed);
            // The type stops using printer compatibility while one link exists (Consumables Inventory Contract 4.3).
            Assert.True((await configuration.UpdateConsumableTypeAsync(noneId, noneName, ConsumableStockModes.Undated, false, false, 91, CancellationToken.None)).Completed);
        }

        await using var context = _database.CreateContext();
        Services.AddSingleton<IOperatorConfiguration>(new OperatorConfiguration(context, NullLogger<OperatorConfiguration>.Instance));
        try
        {
            var cut = Render<ConfigurationPrinters>();
            cut.WaitForAssertion(() => Assert.Contains(printerName, cut.Find("#compatibility-printer").InnerHtml, StringComparison.Ordinal));
            Assert.Contains("Choose a printer model to see its consumables.", cut.Markup, StringComparison.Ordinal);

            cut.Find("#compatibility-printer").Change(printerId.ToString(System.Globalization.CultureInfo.InvariantCulture));

            cut.WaitForAssertion(
                () => Assert.True(cut.FindAll("#compatibility-" + inkId).Count == 1, "Printer compatibility panel: " + cut.Find("#printer-compatibility").OuterHtml),
                TimeSpan.FromSeconds(5));
            var ink = cut.Find("#compatibility-" + inkId);
            var linkedPad = cut.Find("#compatibility-" + linkedPadId);
            Assert.False(ink.HasAttribute("checked"));
            Assert.False(ink.HasAttribute("disabled"));
            Assert.True(linkedPad.HasAttribute("checked"));
            Assert.False(linkedPad.HasAttribute("disabled"));
            Assert.Empty(cut.FindAll("#compatibility-" + otherPadId));
            Assert.True(cut.Markup.IndexOf(usesName, StringComparison.Ordinal) < cut.Markup.IndexOf(noneName, StringComparison.Ordinal));

            ink.Change(true);
            cut.Find("#compatibility-" + linkedPadId).Change(false);
            Assert.True(cut.Find("#compatibility-" + linkedPadId).HasAttribute("disabled"));
            cut.Find("#compatibility-form").Submit();

            cut.WaitForAssertion(() =>
            {
                Assert.True(cut.Find("#compatibility-" + inkId).HasAttribute("checked"));
                Assert.Empty(cut.FindAll("#compatibility-" + linkedPadId));
            });
            await using var check = _database.CreateContext();
            Assert.True(await check.PrinterModelConsumables.AnyAsync(link => link.PrinterModelId == printerId && link.PrintingConsumableId == inkId && link.Active));
            Assert.False(await check.PrinterModelConsumables.AnyAsync(link => link.PrinterModelId == printerId && link.PrintingConsumableId == linkedPadId && link.Active));
        }
        finally
        {
            await using var cleanup = _database.CreateContext();
            cleanup.PrinterModelConsumables.RemoveRange(cleanup.PrinterModelConsumables.Where(link => link.PrinterModelId == printerId));
            cleanup.PrintingConsumables.RemoveRange(cleanup.PrintingConsumables.Where(item => item.ConsumableTypeId == usesId || item.ConsumableTypeId == noneId));
            cleanup.ConsumableTypes.RemoveRange(cleanup.ConsumableTypes.Where(type => type.ConsumableTypeId == usesId || type.ConsumableTypeId == noneId));
            cleanup.PrinterModels.RemoveRange(cleanup.PrinterModels.Where(model => model.PrinterModelId == printerId));
            await cleanup.SaveChangesAsync();
        }
    }
}
