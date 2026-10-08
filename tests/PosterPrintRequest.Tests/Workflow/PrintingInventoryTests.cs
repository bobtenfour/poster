using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
    public async Task Catalog_starts_depleted_and_thresholds_stay_with_the_consumable()
    {
        await ClearInventoryAsync();
        await using var context = _database.CreateContext();
        var inventory = new PrintingInventory(context, NullLogger<PrintingInventory>.Instance);

        var initial = await inventory.LoadAsync(CancellationToken.None);
        Assert.Equal(["C9403A", "C9370A", "C9371A", "C9372A", "C9373A", "C9374A"], initial.Cartridges.Select(item => item.Code).ToArray());
        Assert.All(initial.Cartridges, item =>
        {
            Assert.Equal(0, item.CurrentQuantity);
            Assert.Equal(0, item.LowStockThreshold);
            Assert.Equal(0, item.CriticalStockThreshold);
            Assert.Equal(ConsumableStock.Depleted, item.Status);
            Assert.Equal("130 ml", item.Capacity);
            Assert.True(item.HasExpirationDate);
            Assert.Empty(item.Entries);
            Assert.Null(item.EarliestExpirationDate);
            Assert.Null(item.EarliestExpirationAlert);
        });
        Assert.Equal(["C1861A", "C6814A"], initial.Paper.Select(item => item.Code).ToArray());
        Assert.All(initial.Paper, item =>
        {
            Assert.Equal(ConsumableStock.Depleted, item.Status);
            Assert.False(item.HasExpirationDate);
        });
        Assert.Contains("36 × 150 ft", initial.Paper.Single(item => item.Code == "C1861A").Capacity, StringComparison.Ordinal);
        Assert.Contains("High Gloss", initial.Paper.Single(item => item.Code == "C6814A").Capacity, StringComparison.Ordinal);
        Assert.Empty(initial.LaminatingMaterials);

        var again = await inventory.LoadAsync(CancellationToken.None);
        Assert.Equal(8, again.Cartridges.Count + again.Paper.Count);

        var matte = initial.Cartridges.Single(item => item.Code == "C9403A");
        Assert.Equal("The low-stock threshold must be at least the critical threshold.", (await inventory.UpdateAsync(Thresholds(matte, low: 1, critical: 2), CancellationToken.None)).Message);
        Assert.True((await inventory.UpdateAsync(Thresholds(matte, low: 2, critical: 0), CancellationToken.None)).Completed);

        var added = await inventory.AddLaminatingMaterialAsync("Double-sided film", null, null, CancellationToken.None);
        Assert.True(added.Completed);
        var withMaterial = await inventory.LoadAsync(CancellationToken.None);
        var film = Assert.Single(withMaterial.LaminatingMaterials);
        Assert.Equal(0, film.CurrentQuantity);
        Assert.Equal(ConsumableStock.Depleted, film.Status);
        Assert.True(film.HasExpirationDate);
        Assert.Empty(film.Entries);
        Assert.Null(film.EarliestExpirationDate);
        Assert.Equal("That code is already registered.", (await inventory.AddLaminatingMaterialAsync("Duplicate cartridge code", "C9403A", null, CancellationToken.None)).Message);
    }

    [Fact]
    public async Task Cartridge_entries_keep_distinct_expiration_dates_and_the_total_is_their_sum()
    {
        await ClearInventoryAsync();
        await using var context = _database.CreateContext();
        var inventory = new PrintingInventory(context, NullLogger<PrintingInventory>.Instance);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var initial = await inventory.LoadAsync(CancellationToken.None);
        var matte = initial.Cartridges.Single(item => item.Code == "C9403A");
        var later = today.AddMonths(14);
        var middle = today.AddMonths(8);
        var early = today.AddMonths(3);

        Assert.True((await inventory.AddStockAsync(matte.PrintingConsumableId, 4, later, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(matte.PrintingConsumableId, 2, middle, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(matte.PrintingConsumableId, 1, early, CancellationToken.None)).Completed);

        var stocked = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "C9403A");
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
        var today = DateOnly.FromDateTime(DateTime.Today);
        var initial = await inventory.LoadAsync(CancellationToken.None);
        var matte = initial.Cartridges.Single(item => item.Code == "C9403A");
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

        var unchanged = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "C9403A");
        Assert.Equal(7, unchanged.CurrentQuantity);
        Assert.Equal([1, 2, 4], unchanged.Entries.Select(entry => entry.Quantity).ToArray());

        Assert.True((await inventory.RemoveStockAsync(matte.PrintingConsumableId, 1, true, early, CancellationToken.None)).Completed);
        var afterFirst = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "C9403A");
        Assert.Equal(6, afterFirst.CurrentQuantity);
        Assert.Equal(middle, afterFirst.EarliestExpirationDate);
        Assert.Equal([2, 4], afterFirst.Entries.Select(entry => entry.Quantity).ToArray());
        Assert.Equal([middle, later], afterFirst.Entries.Select(entry => entry.ExpirationDate).ToArray());

        Assert.Contains(middle.ToString("yyyy-MM-dd"), (await inventory.RemoveStockAsync(matte.PrintingConsumableId, 3, true, early, CancellationToken.None)).Message, StringComparison.Ordinal);
        Assert.True((await inventory.RemoveStockAsync(matte.PrintingConsumableId, 3, true, middle, CancellationToken.None)).Completed);
        var spanned = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "C9403A");
        Assert.Equal(3, spanned.CurrentQuantity);
        var remaining = Assert.Single(spanned.Entries);
        Assert.Equal(later, remaining.ExpirationDate);
        Assert.Equal(3, remaining.Quantity);

        Assert.Equal("That removal would reduce the inventory below zero.", (await inventory.RemoveStockAsync(matte.PrintingConsumableId, 4, true, later, CancellationToken.None)).Message);
        Assert.Equal(3, (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "C9403A").CurrentQuantity);
        Assert.True((await inventory.RemoveStockAsync(matte.PrintingConsumableId, 3, true, later, CancellationToken.None)).Completed);
        var removed = (await inventory.LoadAsync(CancellationToken.None)).Cartridges.Single(item => item.Code == "C9403A");
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
        var today = DateOnly.FromDateTime(DateTime.Today);
        var initial = await inventory.LoadAsync(CancellationToken.None);
        var paper = initial.Paper.Single(item => item.Code == "C1861A");
        var dated = today.AddMonths(4);

        Assert.Equal("Enter a quantity greater than zero.", (await inventory.AddStockAsync(paper.PrintingConsumableId, 0, null, CancellationToken.None)).Message);
        Assert.Equal("This material does not use an expiration date.", (await inventory.AddStockAsync(paper.PrintingConsumableId, 1, today, CancellationToken.None)).Message);
        Assert.True((await inventory.AddStockAsync(paper.PrintingConsumableId, 2, null, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(paper.PrintingConsumableId, 3, null, CancellationToken.None)).Completed);
        var stockedPaper = (await inventory.LoadAsync(CancellationToken.None)).Paper.Single(item => item.Code == "C1861A");
        Assert.Equal(5, stockedPaper.CurrentQuantity);
        Assert.Null(stockedPaper.EarliestExpirationDate);
        Assert.Equal(2, stockedPaper.Entries.Count);
        Assert.All(stockedPaper.Entries, entry => Assert.Null(entry.ExpirationDate));

        Assert.Equal("That removal would reduce the inventory below zero.", (await inventory.RemoveStockAsync(paper.PrintingConsumableId, 6, false, null, CancellationToken.None)).Message);
        Assert.True((await inventory.RemoveStockAsync(paper.PrintingConsumableId, 4, false, null, CancellationToken.None)).Completed);
        var reducedPaper = (await inventory.LoadAsync(CancellationToken.None)).Paper.Single(item => item.Code == "C1861A");
        var paperEntry = Assert.Single(reducedPaper.Entries);
        Assert.Equal(1, paperEntry.Quantity);
        Assert.Null(paperEntry.ExpirationDate);
        Assert.Equal(ConsumableStock.InStock, reducedPaper.Status);

        Assert.True((await inventory.AddLaminatingMaterialAsync("Double-sided film", null, null, CancellationToken.None)).Completed);
        var material = (await inventory.LoadAsync(CancellationToken.None)).LaminatingMaterials.Single();
        Assert.True((await inventory.AddStockAsync(material.PrintingConsumableId, 2, null, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(material.PrintingConsumableId, 1, dated, CancellationToken.None)).Completed);
        var stockedMaterial = (await inventory.LoadAsync(CancellationToken.None)).LaminatingMaterials.Single();
        Assert.Equal(3, stockedMaterial.CurrentQuantity);
        Assert.Equal(dated, stockedMaterial.EarliestExpirationDate);
        Assert.Equal(ConsumableStock.ExpiresWithinSixMonths, stockedMaterial.EarliestExpirationAlert);
        Assert.Contains(stockedMaterial.Entries, entry => entry.ExpirationDate is null && entry.Quantity == 2 && entry.ExpirationAlert is null);
        Assert.True((await inventory.RemoveStockAsync(material.PrintingConsumableId, 1, false, null, CancellationToken.None)).Completed);
        var afterRemoval = (await inventory.LoadAsync(CancellationToken.None)).LaminatingMaterials.Single();
        var undated = Assert.Single(afterRemoval.Entries);
        Assert.Null(undated.ExpirationDate);
        Assert.Equal(2, undated.Quantity);
    }

    [Fact]
    public async Task Expiration_alerts_and_dashboard_counts_follow_the_stock_entries()
    {
        await ClearInventoryAsync();
        await using var context = _database.CreateContext();
        var inventory = new PrintingInventory(context, NullLogger<PrintingInventory>.Instance);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var initial = await inventory.LoadAsync(CancellationToken.None);
        var matte = initial.Cartridges.Single(item => item.Code == "C9403A");
        var blue = initial.Cartridges.Single(item => item.Code == "C9371A");
        var photoBlack = initial.Cartridges.Single(item => item.Code == "C9370A");
        var yellow = initial.Cartridges.Single(item => item.Code == "C9373A");
        var photo = initial.Paper.Single(item => item.Code == "C6814A");

        Assert.True((await inventory.UpdateAsync(Thresholds(matte, low: 2, critical: 0), CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(matte.PrintingConsumableId, 1, today.AddMonths(6), CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(blue.PrintingConsumableId, 3, today.AddMonths(12), CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(photoBlack.PrintingConsumableId, 4, today.AddMonths(12).AddDays(1), CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(yellow.PrintingConsumableId, 1, today, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(yellow.PrintingConsumableId, 1, today.AddMonths(4), CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(photo.PrintingConsumableId, 1, null, CancellationToken.None)).Completed);

        var loaded = await inventory.LoadAsync(CancellationToken.None);
        var stockedMatte = loaded.Cartridges.Single(item => item.Code == "C9403A");
        var stockedBlue = loaded.Cartridges.Single(item => item.Code == "C9371A");
        var stockedPhotoBlack = loaded.Cartridges.Single(item => item.Code == "C9370A");
        var stockedYellow = loaded.Cartridges.Single(item => item.Code == "C9373A");
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
        Assert.Equal("C9403A", alerts.LowStock[0].Code);
        Assert.Equal(3, alerts.ExpiringSoon.Count);
        Assert.Contains(alerts.ExpiringSoon, alert => alert.Code == "C9403A" && alert.Detail.StartsWith(ConsumableStock.ExpiresWithinSixMonths, StringComparison.Ordinal));
        Assert.Contains(alerts.ExpiringSoon, alert => alert.Code == "C9371A" && alert.Detail.StartsWith(ConsumableStock.ExpiresWithinTwelveMonths, StringComparison.Ordinal));
        var yellowAlert = alerts.ExpiringSoon.Single(alert => alert.Code == "C9373A");
        Assert.StartsWith(ConsumableStock.ExpiresWithinSixMonths, yellowAlert.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(ConsumableStock.ExpiresWithinTwelveMonths, yellowAlert.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(alerts.ExpiringSoon, alert => alert.Code == "C9370A");
        Assert.DoesNotContain(alerts.ExpiringSoon, alert => alert.Code == "C6814A");
        Assert.DoesNotContain(alerts.Depleted, alert => alert.Code == "C6814A");
    }

    private async Task ClearInventoryAsync()
    {
        await using var context = _database.CreateContext();
        await context.PrintingStockEntries.ExecuteDeleteAsync();
        await context.PrintingConsumables.ExecuteDeleteAsync();
    }

    private static ConsumableUpdate Thresholds(ConsumableRow row, int low, int critical) => new()
    {
        PrintingConsumableId = row.PrintingConsumableId,
        LowStockThreshold = low,
        CriticalStockThreshold = critical,
        Name = row.Name,
        Code = row.Code,
        Capacity = row.Capacity
    };
}
