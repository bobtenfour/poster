using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

[Collection("Workflow database")]
public sealed class PrintingInventoryTests
{
    private readonly WorkflowDatabaseFixture _database;

    public PrintingInventoryTests(WorkflowDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Configured_consumable_starts_depleted_and_thresholds_stay_with_the_consumable()
    {
        await ClearInventoryAsync();
        await using var context = _database.CreateContext();
        var inventory = new PrintingInventory(context, NullLogger<PrintingInventory>.Instance);
        var configuration = new OperatorConfiguration(context, NullLogger<OperatorConfiguration>.Instance);

        var empty = await inventory.LoadAsync(CancellationToken.None);
        Assert.Empty(empty.Cartridges);
        Assert.Empty(empty.Paper);
        Assert.Empty(empty.LaminatingMaterials);
        Assert.Equal(0, await context.PrintingConsumables.CountAsync());

        var cartridge = await AddAsync(configuration, inventory, ConsumableCategory.Cartridge, "Matte black", "CART-1", "130 ml");
        var paper = await AddAsync(configuration, inventory, ConsumableCategory.Paper, "Bright white", "PAPER-1", "36 in");
        Assert.Equal(0, cartridge.CurrentQuantity);
        Assert.Equal(0, cartridge.LowStockThreshold);
        Assert.Equal(0, cartridge.CriticalStockThreshold);
        Assert.Equal(ConsumableStock.Depleted, cartridge.Status);
        Assert.Equal("130 ml", cartridge.Capacity);
        Assert.True(cartridge.HasExpirationDate);
        Assert.Empty(cartridge.Entries);
        Assert.Null(cartridge.EarliestExpirationDate);
        Assert.Null(cartridge.EarliestExpirationAlert);
        Assert.Equal(ConsumableStock.Depleted, paper.Status);
        Assert.False(paper.HasExpirationDate);
        Assert.Equal("36 in", paper.Capacity);

        var again = await inventory.LoadAsync(CancellationToken.None);
        Assert.Single(again.Cartridges);
        Assert.Single(again.Paper);

        Assert.Equal("The low-stock threshold must be at least the critical threshold.", (await configuration.SetConsumableThresholdsAsync(cartridge.PrintingConsumableId, 1, 2, CancellationToken.None)).Message);
        Assert.True((await configuration.SetConsumableThresholdsAsync(cartridge.PrintingConsumableId, 2, 0, CancellationToken.None)).Completed);
        var configured = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "CART-1");
        Assert.Equal(2, configured.LowStockThreshold);
        Assert.Equal(0, configured.CriticalStockThreshold);
        Assert.Equal("Matte black", configured.Name);

        var added = await configuration.AddConsumableAsync(ConsumableCategory.Laminating, "Double-sided film", null, null, CancellationToken.None);
        Assert.True(added.Completed);
        var withMaterial = await inventory.LoadAsync(CancellationToken.None);
        var film = Assert.Single(withMaterial.LaminatingMaterials);
        Assert.Equal(0, film.CurrentQuantity);
        Assert.Equal(ConsumableStock.Depleted, film.Status);
        Assert.True(film.HasExpirationDate);
        Assert.Empty(film.Entries);
        Assert.Null(film.EarliestExpirationDate);
        Assert.Equal("That code is already registered.", (await configuration.AddConsumableAsync(ConsumableCategory.Laminating, "Duplicate cartridge code", "CART-1", null, CancellationToken.None)).Message);
    }

    [Fact]
    public async Task Same_expiration_aggregates_and_a_different_date_stays_separate()
    {
        await ClearInventoryAsync();
        await using var context = _database.CreateContext();
        var inventory = new PrintingInventory(context, NullLogger<PrintingInventory>.Instance);
        var configuration = new OperatorConfiguration(context, NullLogger<OperatorConfiguration>.Instance);
        var blue = await AddAsync(configuration, inventory, ConsumableCategory.Cartridge, "B Blue", "CART-BLUE-GROUP", null);
        var march = new DateOnly(2027, 3, 15);
        var july = new DateOnly(2027, 7, 20);
        var before = await context.PrintingConsumables.AsNoTracking().SingleAsync(item => item.PrintingConsumableId == blue.PrintingConsumableId);

        Assert.True((await inventory.AddStockAsync(blue.PrintingConsumableId, 10, march, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(blue.PrintingConsumableId, 5, march, CancellationToken.None)).Completed);
        var grouped = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "CART-BLUE-GROUP");
        var marchEntry = Assert.Single(grouped.Entries);
        Assert.Equal(15, marchEntry.Quantity);
        Assert.Equal(march, marchEntry.ExpirationDate);
        Assert.Equal(15, grouped.CurrentQuantity);
        Assert.Equal(march, grouped.EarliestExpirationDate);

        Assert.True((await inventory.AddStockAsync(blue.PrintingConsumableId, 4, july, CancellationToken.None)).Completed);
        var split = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "CART-BLUE-GROUP");
        Assert.Equal(19, split.CurrentQuantity);
        Assert.Equal(march, split.EarliestExpirationDate);
        Assert.Equal([march, july], split.Entries.Select(entry => entry.ExpirationDate).ToArray());
        Assert.Equal([15, 4], split.Entries.Select(entry => entry.Quantity).ToArray());

        context.PrintingStockEntries.Add(new PrintingStockEntry
        {
            PrintingConsumableId = blue.PrintingConsumableId,
            Quantity = 0,
            ExpirationDate = new DateOnly(2027, 1, 1)
        });
        await context.SaveChangesAsync();
        var withZero = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "CART-BLUE-GROUP");
        Assert.Equal(march, withZero.EarliestExpirationDate);
        Assert.DoesNotContain(withZero.Entries, entry => entry.Quantity == 0);
        Assert.Equal(2, await context.PrintingStockEntries.CountAsync(entry => entry.PrintingConsumableId == blue.PrintingConsumableId && entry.Quantity > 0));

        var after = await context.PrintingConsumables.AsNoTracking().SingleAsync(item => item.PrintingConsumableId == blue.PrintingConsumableId);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Code, after.Code);
        Assert.Equal(before.Capacity, after.Capacity);
        Assert.Equal(before.LowStockThreshold, after.LowStockThreshold);
        Assert.Equal(before.CriticalStockThreshold, after.CriticalStockThreshold);
        Assert.Equal(before.Active, after.Active);
        Assert.Equal(before.Category, after.Category);
    }

    [Fact]
    public async Task Cartridge_entries_keep_distinct_expiration_dates_and_the_total_is_their_sum()
    {
        await ClearInventoryAsync();
        await using var context = _database.CreateContext();
        var inventory = new PrintingInventory(context, NullLogger<PrintingInventory>.Instance);
        var configuration = new OperatorConfiguration(context, NullLogger<OperatorConfiguration>.Instance);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var matte = await AddAsync(configuration, inventory, ConsumableCategory.Cartridge, "Matte black", "CART-MATTE", "130 ml");
        var later = today.AddMonths(14);
        var middle = today.AddMonths(8);
        var early = today.AddMonths(3);

        Assert.True((await inventory.AddStockAsync(matte.PrintingConsumableId, 4, later, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(matte.PrintingConsumableId, 2, middle, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(matte.PrintingConsumableId, 1, early, CancellationToken.None)).Completed);

        var stocked = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "CART-MATTE");
        Assert.Equal(7, stocked.CurrentQuantity);
        Assert.Equal(early, stocked.EarliestExpirationDate);
        Assert.Equal(ConsumableStock.ExpiresWithinSixMonths, stocked.EarliestExpirationAlert);
        Assert.Equal([early, middle, later], stocked.Entries.Select(entry => entry.ExpirationDate).ToArray());
        Assert.Equal([1, 2, 4], stocked.Entries.Select(entry => entry.Quantity).ToArray());
    }

    [Fact]
    public async Task Removal_uses_the_earliest_expiration_after_acknowledgement_and_can_span_entries()
    {
        await ClearInventoryAsync();
        await using var context = _database.CreateContext();
        var inventory = new PrintingInventory(context, NullLogger<PrintingInventory>.Instance);
        var configuration = new OperatorConfiguration(context, NullLogger<OperatorConfiguration>.Instance);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var matte = await AddAsync(configuration, inventory, ConsumableCategory.Cartridge, "Matte black", "CART-MATTE", "130 ml");
        var early = today.AddMonths(3);
        var middle = today.AddMonths(8);
        var later = today.AddMonths(14);

        Assert.True((await inventory.AddStockAsync(matte.PrintingConsumableId, 1, early, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(matte.PrintingConsumableId, 2, middle, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(matte.PrintingConsumableId, 4, later, CancellationToken.None)).Completed);

        Assert.Equal("Enter a quantity greater than zero.", (await inventory.RemoveStockAsync(matte.PrintingConsumableId, 0, true, early, CancellationToken.None)).Message);
        var missingAcknowledgement = await inventory.RemoveStockAsync(matte.PrintingConsumableId, 1, false, early, CancellationToken.None);
        Assert.Equal("Acknowledge that " + early.ToString("yyyy-MM-dd") + " is the first expiration before removing this cartridge.", missingAcknowledgement.Message);
        var wrongDate = await inventory.RemoveStockAsync(matte.PrintingConsumableId, 1, true, middle, CancellationToken.None);
        Assert.Equal(missingAcknowledgement.Message, wrongDate.Message);
        Assert.Equal("That removal would reduce the inventory below zero.", (await inventory.RemoveStockAsync(matte.PrintingConsumableId, 8, true, early, CancellationToken.None)).Message);

        var unchanged = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "CART-MATTE");
        Assert.Equal(7, unchanged.CurrentQuantity);
        Assert.Equal([1, 2, 4], unchanged.Entries.Select(entry => entry.Quantity).ToArray());

        Assert.True((await inventory.RemoveStockAsync(matte.PrintingConsumableId, 1, true, early, CancellationToken.None)).Completed);
        var afterFirst = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "CART-MATTE");
        Assert.Equal(6, afterFirst.CurrentQuantity);
        Assert.Equal(middle, afterFirst.EarliestExpirationDate);
        Assert.Equal([2, 4], afterFirst.Entries.Select(entry => entry.Quantity).ToArray());
        Assert.Equal([middle, later], afterFirst.Entries.Select(entry => entry.ExpirationDate).ToArray());

        Assert.Contains(middle.ToString("yyyy-MM-dd"), (await inventory.RemoveStockAsync(matte.PrintingConsumableId, 3, true, early, CancellationToken.None)).Message, StringComparison.Ordinal);
        Assert.True((await inventory.RemoveStockAsync(matte.PrintingConsumableId, 3, true, middle, CancellationToken.None)).Completed);
        var spanned = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "CART-MATTE");
        Assert.Equal(3, spanned.CurrentQuantity);
        var remaining = Assert.Single(spanned.Entries);
        Assert.Equal(later, remaining.ExpirationDate);
        Assert.Equal(3, remaining.Quantity);

        Assert.Equal("That removal would reduce the inventory below zero.", (await inventory.RemoveStockAsync(matte.PrintingConsumableId, 4, true, later, CancellationToken.None)).Message);
        Assert.Equal(3, (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "CART-MATTE").CurrentQuantity);
        Assert.True((await inventory.RemoveStockAsync(matte.PrintingConsumableId, 3, true, later, CancellationToken.None)).Completed);
        var removed = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "CART-MATTE");
        Assert.Equal(0, removed.CurrentQuantity);
        Assert.Empty(removed.Entries);
        Assert.Equal(ConsumableStock.Depleted, removed.Status);
        Assert.Null(removed.EarliestExpirationDate);
    }

    [Fact]
    public async Task Paper_has_no_expiration_and_laminating_expiration_is_optional()
    {
        await ClearInventoryAsync();
        await using var context = _database.CreateContext();
        var inventory = new PrintingInventory(context, NullLogger<PrintingInventory>.Instance);
        var configuration = new OperatorConfiguration(context, NullLogger<OperatorConfiguration>.Instance);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var paper = await AddAsync(configuration, inventory, ConsumableCategory.Paper, "Bright white", "PAPER-1", null);
        var dated = today.AddMonths(4);

        Assert.Equal("Enter a quantity greater than zero.", (await inventory.AddStockAsync(paper.PrintingConsumableId, 0, null, CancellationToken.None)).Message);
        Assert.Equal("This material does not use an expiration date.", (await inventory.AddStockAsync(paper.PrintingConsumableId, 1, today, CancellationToken.None)).Message);
        Assert.True((await inventory.AddStockAsync(paper.PrintingConsumableId, 2, null, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(paper.PrintingConsumableId, 3, null, CancellationToken.None)).Completed);
        var stockedPaper = (await inventory.LoadAsync(CancellationToken.None)).Paper.Single(item => item.Code == "PAPER-1");
        Assert.Equal(5, stockedPaper.CurrentQuantity);
        Assert.Null(stockedPaper.EarliestExpirationDate);
        var paperReceipt = Assert.Single(stockedPaper.Entries);
        Assert.Equal(5, paperReceipt.Quantity);
        Assert.Null(paperReceipt.ExpirationDate);

        Assert.Equal("That removal would reduce the inventory below zero.", (await inventory.RemoveStockAsync(paper.PrintingConsumableId, 6, false, null, CancellationToken.None)).Message);
        Assert.True((await inventory.RemoveStockAsync(paper.PrintingConsumableId, 4, false, null, CancellationToken.None)).Completed);
        var reducedPaper = (await inventory.LoadAsync(CancellationToken.None)).Paper.Single(item => item.Code == "PAPER-1");
        var paperEntry = Assert.Single(reducedPaper.Entries);
        Assert.Equal(1, paperEntry.Quantity);
        Assert.Null(paperEntry.ExpirationDate);
        Assert.Equal(ConsumableStock.InStock, reducedPaper.Status);

        Assert.True((await configuration.AddConsumableAsync(ConsumableCategory.Laminating, "Double-sided film", null, null, CancellationToken.None)).Completed);
        var material = (await inventory.LoadAsync(CancellationToken.None)).LaminatingMaterials.Single();
        Assert.True((await inventory.AddStockAsync(material.PrintingConsumableId, 2, null, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(material.PrintingConsumableId, 3, null, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(material.PrintingConsumableId, 1, dated, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(material.PrintingConsumableId, 4, dated, CancellationToken.None)).Completed);
        var stockedMaterial = (await inventory.LoadAsync(CancellationToken.None)).LaminatingMaterials.Single();
        Assert.Equal(10, stockedMaterial.CurrentQuantity);
        Assert.Equal(dated, stockedMaterial.EarliestExpirationDate);
        Assert.Equal(ConsumableStock.ExpiresWithinSixMonths, stockedMaterial.EarliestExpirationAlert);
        Assert.Equal(2, stockedMaterial.Entries.Count);
        Assert.Contains(stockedMaterial.Entries, entry => entry.ExpirationDate == dated && entry.Quantity == 5);
        Assert.Contains(stockedMaterial.Entries, entry => entry.ExpirationDate is null && entry.Quantity == 5 && entry.ExpirationAlert is null);
        Assert.True((await inventory.RemoveStockAsync(material.PrintingConsumableId, 1, false, null, CancellationToken.None)).Completed);
        var afterRemoval = (await inventory.LoadAsync(CancellationToken.None)).LaminatingMaterials.Single();
        Assert.Equal(dated, afterRemoval.EarliestExpirationDate);
        Assert.Contains(afterRemoval.Entries, entry => entry.ExpirationDate == dated && entry.Quantity == 4);
        Assert.Contains(afterRemoval.Entries, entry => entry.ExpirationDate is null && entry.Quantity == 5);
    }

    [Fact]
    public async Task Expiration_alerts_and_dashboard_counts_follow_the_stock_entries()
    {
        await ClearInventoryAsync();
        await using var context = _database.CreateContext();
        var inventory = new PrintingInventory(context, NullLogger<PrintingInventory>.Instance);
        var configuration = new OperatorConfiguration(context, NullLogger<OperatorConfiguration>.Instance);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var matte = await AddAsync(configuration, inventory, ConsumableCategory.Cartridge, "Matte black", "CART-MATTE", null);
        var blue = await AddAsync(configuration, inventory, ConsumableCategory.Cartridge, "Blue", "CART-BLUE", null);
        var photoBlack = await AddAsync(configuration, inventory, ConsumableCategory.Cartridge, "Photo black", "CART-BLACK", null);
        var yellow = await AddAsync(configuration, inventory, ConsumableCategory.Cartridge, "Yellow", "CART-YELLOW", null);
        await AddAsync(configuration, inventory, ConsumableCategory.Cartridge, "Extra one", "CART-EXTRA-1", null);
        await AddAsync(configuration, inventory, ConsumableCategory.Cartridge, "Extra two", "CART-EXTRA-2", null);
        await AddAsync(configuration, inventory, ConsumableCategory.Paper, "Plain", "PAPER-PLAIN", null);
        var photo = await AddAsync(configuration, inventory, ConsumableCategory.Paper, "Gloss", "PAPER-GLOSS", null);

        Assert.True((await configuration.SetConsumableThresholdsAsync(matte.PrintingConsumableId, 2, 0, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(matte.PrintingConsumableId, 1, today.AddMonths(6), CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(blue.PrintingConsumableId, 3, today.AddMonths(12), CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(photoBlack.PrintingConsumableId, 4, today.AddMonths(12).AddDays(1), CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(yellow.PrintingConsumableId, 1, today, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(yellow.PrintingConsumableId, 1, today.AddMonths(4), CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(photo.PrintingConsumableId, 1, null, CancellationToken.None)).Completed);

        var loaded = await inventory.LoadAsync(CancellationToken.None);
        var stockedMatte = loaded.Cartridges.Single(item => item.Code == "CART-MATTE");
        var stockedBlue = loaded.Cartridges.Single(item => item.Code == "CART-BLUE");
        var stockedPhotoBlack = loaded.Cartridges.Single(item => item.Code == "CART-BLACK");
        var stockedYellow = loaded.Cartridges.Single(item => item.Code == "CART-YELLOW");
        Assert.Equal(ConsumableStock.ExpiresWithinSixMonths, Assert.Single(stockedMatte.Entries).ExpirationAlert);
        Assert.Equal(ConsumableStock.ExpiresWithinTwelveMonths, Assert.Single(stockedBlue.Entries).ExpirationAlert);
        Assert.Null(Assert.Single(stockedPhotoBlack.Entries).ExpirationAlert);
        Assert.Equal(ConsumableStock.Expired, stockedYellow.Entries[0].ExpirationAlert);
        Assert.Equal(ConsumableStock.ExpiresWithinSixMonths, stockedYellow.Entries[1].ExpirationAlert);
        Assert.Equal(ConsumableStock.Expired, stockedYellow.EarliestExpirationAlert);
        Assert.Equal(2, stockedYellow.CurrentQuantity);

        var alerts = await inventory.CriticalAlertsAsync(CancellationToken.None);
        Assert.Equal(3, alerts.Depleted.Count);
        Assert.Single(alerts.LowStock);
        Assert.Equal("CART-MATTE", alerts.LowStock[0].Code);
        Assert.Equal(3, alerts.ExpiringSoon.Count);
        Assert.Contains(alerts.ExpiringSoon, alert => alert.Code == "CART-MATTE" && alert.Detail.StartsWith(ConsumableStock.ExpiresWithinSixMonths, StringComparison.Ordinal));
        Assert.Contains(alerts.ExpiringSoon, alert => alert.Code == "CART-BLUE" && alert.Detail.StartsWith(ConsumableStock.ExpiresWithinTwelveMonths, StringComparison.Ordinal));
        var yellowAlert = alerts.ExpiringSoon.Single(alert => alert.Code == "CART-YELLOW");
        Assert.StartsWith(ConsumableStock.ExpiresWithinSixMonths, yellowAlert.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(ConsumableStock.ExpiresWithinTwelveMonths, yellowAlert.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(alerts.ExpiringSoon, alert => alert.Code == "CART-BLACK");
        Assert.DoesNotContain(alerts.ExpiringSoon, alert => alert.Code == "PAPER-GLOSS");
        Assert.DoesNotContain(alerts.Depleted, alert => alert.Code == "PAPER-GLOSS");
    }

    private async Task ClearInventoryAsync()
    {
        await using var context = _database.CreateContext();
        await context.PrinterModelConsumables.ExecuteDeleteAsync();
        await context.PrintingStockEntries.ExecuteDeleteAsync();
        await context.PrintingConsumables.ExecuteDeleteAsync();
    }

    private static async Task<ConsumableRow> AddAsync(
        OperatorConfiguration configuration,
        PrintingInventory inventory,
        string category,
        string name,
        string code,
        string? capacity)
    {
        var outcome = await configuration.AddConsumableAsync(category, name, code, capacity, CancellationToken.None);
        Assert.True(outcome.Completed, outcome.Message);
        var view = await inventory.LoadAsync(CancellationToken.None);
        var rows = category switch
        {
            ConsumableCategory.Cartridge => view.Cartridges,
            ConsumableCategory.Paper => view.Paper,
            _ => view.LaminatingMaterials
        };
        return rows.Single(item => item.Code == code);
    }
}

public sealed class PrintingInventorySourceTests
{
    [Fact]
    public void Inventory_does_not_edit_consumable_configuration()
    {
        var root = Path.Combine(PosterPrintRequest.Tests.RepositoryPaths.Root(), "src", "PosterPrintRequest.Web");
        var inventoryPage = File.ReadAllText(Path.Combine(root, "Components", "Pages", "TechnicianInventory.razor"));
        var table = File.ReadAllText(Path.Combine(root, "Components", "Pages", "ConsumableStockTable.razor"));
        var inventory = File.ReadAllText(Path.Combine(root, "Requests", "PrintingInventory.cs"));
        var configurationPage = File.ReadAllText(Path.Combine(root, "Components", "Pages", "TechnicianConfiguration.razor"));

        Assert.Contains("Total quantity", table, StringComparison.Ordinal);
        Assert.Contains("First expiration", table, StringComparison.Ordinal);
        Assert.DoesNotContain(">Save<", inventoryPage, StringComparison.Ordinal);
        Assert.DoesNotContain(">Save<", table, StringComparison.Ordinal);
        Assert.DoesNotContain("critical threshold", inventoryPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("low-stock threshold", inventoryPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Add material", inventoryPage, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateAsync", inventory, StringComparison.Ordinal);
        Assert.DoesNotContain("AddLaminatingMaterialAsync", inventory, StringComparison.Ordinal);
        Assert.Contains("epx-config-table", configurationPage, StringComparison.Ordinal);
        Assert.Contains("epx-config-filters", configurationPage, StringComparison.Ordinal);
        Assert.DoesNotContain(">Status</label>", configurationPage, StringComparison.Ordinal);
        Assert.Contains("critical threshold", configurationPage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("low-stock threshold", configurationPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("epx-config-row", configurationPage, StringComparison.Ordinal);
        Assert.DoesNotContain(">Edit</button>", configurationPage, StringComparison.Ordinal);
    }
}
