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
    public async Task Catalog_starts_depleted_and_inventory_owns_stock_expiration_and_alerts()
    {
        await ClearInventoryAsync();
        await using var context = _database.CreateContext();
        var inventory = new PrintingInventory(context, NullLogger<PrintingInventory>.Instance);
        var today = new DateOnly(2026, 10, 7);

        var initial = await inventory.LoadAsync(CancellationToken.None);
        Assert.Null(initial.ExpirationWarningDays);
        Assert.Equal(["C9403A", "C9370A", "C9371A", "C9372A", "C9373A", "C9374A"], initial.Cartridges.Select(item => item.Code).ToArray());
        Assert.All(initial.Cartridges, item =>
        {
            Assert.Equal(0, item.CurrentQuantity);
            Assert.Equal(0, item.LowStockThreshold);
            Assert.Equal(0, item.CriticalStockThreshold);
            Assert.Equal(ConsumableStock.Depleted, item.Status);
            Assert.Equal("130 ml", item.Capacity);
            Assert.True(item.HasExpirationDate);
            Assert.Null(item.ExpirationDate);
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
        var paper = initial.Paper.Single(item => item.Code == "C1861A");
        Assert.Equal("Enter the expiration date.", (await inventory.UpdateAsync(Change(matte, quantity: 2), CancellationToken.None)).Message);
        Assert.Equal("This material does not use an expiration date.", (await inventory.UpdateAsync(Change(paper, quantity: 1, expiration: today), CancellationToken.None)).Message);
        Assert.Equal("The low-stock threshold must be at least the critical threshold.", (await inventory.UpdateAsync(Change(matte, low: 1, critical: 2), CancellationToken.None)).Message);

        Assert.True((await inventory.UpdateAsync(Change(matte, quantity: 1, low: 2, critical: 0, expiration: today.AddDays(10)), CancellationToken.None)).Completed);
        Assert.True((await inventory.UpdateAsync(Change(paper, quantity: 4, low: 1, critical: 0), CancellationToken.None)).Completed);
        Assert.True((await inventory.SetExpirationWarningDaysAsync(30, CancellationToken.None)).Completed);

        var alerts = await inventory.CriticalAlertsAsync(today, CancellationToken.None);
        Assert.Contains(alerts.LowStock, alert => alert.Code == "C9403A" && alert.Detail.Contains("quantity 1", StringComparison.Ordinal));
        Assert.DoesNotContain(alerts.Depleted, alert => alert.Code == "C9403A");
        Assert.DoesNotContain(alerts.Depleted, alert => alert.Code == "C1861A");
        Assert.DoesNotContain(alerts.LowStock, alert => alert.Code == "C1861A");
        Assert.Contains(alerts.ExpiringSoon, alert => alert.Code == "C9403A" && alert.Detail.StartsWith("Expiring soon", StringComparison.Ordinal));
        Assert.DoesNotContain(alerts.ExpiringSoon, alert => alert.Code == "C1861A");
        Assert.Contains(alerts.Depleted, alert => alert.Code == "C9370A");

        Assert.True((await inventory.UpdateAsync(Change(matte, quantity: 1, low: 2, critical: 0, expiration: today.AddDays(-1)), CancellationToken.None)).Completed);
        Assert.True((await inventory.SetExpirationWarningDaysAsync(null, CancellationToken.None)).Completed);
        var withoutPeriod = await inventory.CriticalAlertsAsync(today, CancellationToken.None);
        Assert.Contains(withoutPeriod.ExpiringSoon, alert => alert.Code == "C9403A" && alert.Detail.StartsWith("Expired", StringComparison.Ordinal));

        var added = await inventory.AddLaminatingMaterialAsync("Double-sided film", null, null, CancellationToken.None);
        Assert.True(added.Completed);
        var withMaterial = await inventory.LoadAsync(CancellationToken.None);
        var film = Assert.Single(withMaterial.LaminatingMaterials);
        Assert.Equal(0, film.CurrentQuantity);
        Assert.Equal(ConsumableStock.Depleted, film.Status);
        Assert.True(film.HasExpirationDate);
        Assert.Null(film.ExpirationDate);
        Assert.Equal("That code is already registered.", (await inventory.AddLaminatingMaterialAsync("Duplicate cartridge code", "C9403A", null, CancellationToken.None)).Message);
    }

    private async Task ClearInventoryAsync()
    {
        await using var context = _database.CreateContext();
        await context.PrintingConsumables.ExecuteDeleteAsync();
        await context.PrintingInventorySettings.ExecuteDeleteAsync();
    }

    private static ConsumableUpdate Change(ConsumableRow row, int quantity = 0, int low = 0, int critical = 0, DateOnly? expiration = null) => new()
    {
        PrintingConsumableId = row.PrintingConsumableId,
        CurrentQuantity = quantity,
        LowStockThreshold = low,
        CriticalStockThreshold = critical,
        ExpirationDate = expiration,
        Name = row.Name,
        Code = row.Code,
        Capacity = row.Capacity
    };
}
