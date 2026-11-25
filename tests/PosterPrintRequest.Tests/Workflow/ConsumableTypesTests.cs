using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence.Migrations;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

/// <summary>Consumables Inventory Contract (PHASE 16), acceptance section 9, against SQL Server.</summary>
[Collection("Workflow database")]
public sealed class ConsumableTypesTests
{
    private readonly WorkflowDatabaseFixture _database;

    public ConsumableTypesTests(WorkflowDatabaseFixture database)
    {
        _database = database;
    }

    // 9.1 and owner decisions D2, D4, D5.
    [Fact]
    public async Task The_migration_creates_the_four_initial_types()
    {
        await ResetAsync();
        await using var context = _database.CreateContext();
        var types = await context.ConsumableTypes.AsNoTracking().OrderBy(type => type.DisplayOrder).ToListAsync();

        Assert.Equal([InitialTypes.Cartridge, InitialTypes.Paper, InitialTypes.Laminating, InitialTypes.PrinterHeads], types.Select(type => type.Name).ToArray());
        Assert.Equal(
            [ConsumableStockModes.DateRequired, ConsumableStockModes.Undated, ConsumableStockModes.DateOptional, ConsumableStockModes.DateRequired],
            types.Select(type => type.StockMode).ToArray());
        Assert.Equal([true, false, false, true], types.Select(type => type.AcknowledgeEarliestExpiration).ToArray());
        Assert.All(types, type => Assert.True(type.UsesPrinterCompatibility));
        Assert.All(types, type => Assert.True(type.Active));
        Assert.Equal(types.Select(type => type.Name.ToUpperInvariant()), types.Select(type => type.NameKey));
    }

    // 5.1, 5.2 and 9.3a: exact comparison, trailing spaces and case significant.
    [Theory]
    [InlineData("Cartridge", true)]
    [InlineData("Paper", true)]
    [InlineData("Laminating", true)]
    [InlineData("Cartridge ", false)]
    [InlineData(" Cartridge", false)]
    [InlineData("cartridge", false)]
    [InlineData("PAPER", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Laminating ", false)]
    public async Task The_migration_recognizes_only_the_exact_values(string? value, bool recognized)
    {
        await using var context = _database.CreateContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN " + AddConfigurableConsumableTypes.Recognized
            + " THEN 1 ELSE 0 END FROM (SELECT CAST(@value AS nvarchar(32)) AS [Category]) AS [Row]";
        command.Parameters.Add(new SqlParameter("@value", System.Data.SqlDbType.NVarChar, 32) { Value = (object?)value ?? DBNull.Value });
        Assert.Equal(recognized ? 1 : 0, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    // 4.1a and 9.3a: one uniqueness rule, owned by the database.
    [Fact]
    public async Task Type_names_collide_by_case_only_and_the_index_decides_under_concurrency()
    {
        await ResetAsync();
        await using var context = _database.CreateContext();
        var configuration = Configuration(context);

        Assert.Equal(OperatorConfiguration.TypeNameTaken, (await configuration.AddConsumableTypeAsync("CARTRIDGES", ConsumableStockModes.DateRequired, false, true, 9, CancellationToken.None)).Message);
        Assert.True((await configuration.AddConsumableTypeAsync("Toner", ConsumableStockModes.Undated, false, false, 5, CancellationToken.None)).Completed);
        Assert.True((await configuration.AddConsumableTypeAsync("Tóner", ConsumableStockModes.Undated, false, false, 6, CancellationToken.None)).Completed);
        Assert.Equal("Earliest-date acknowledgement needs an expiration date.", (await configuration.AddConsumableTypeAsync("Ink pads", ConsumableStockModes.Undated, true, false, 7, CancellationToken.None)).Message);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var parallel = _database.CreateContext();
            return await Configuration(parallel).AddConsumableTypeAsync("Race type", ConsumableStockModes.Undated, false, true, 8, CancellationToken.None);
        }));
        Assert.Single(results, result => result.Completed);
        Assert.All(results.Where(result => !result.Completed), result => Assert.Equal(OperatorConfiguration.TypeNameTaken, result.Message));
        await using var check = _database.CreateContext();
        Assert.Equal(1, await check.ConsumableTypes.CountAsync(type => type.Name == "Race type"));

        await using var raw = _database.CreateContext();
        await Assert.ThrowsAnyAsync<DbUpdateException>(async () =>
        {
            raw.ConsumableTypes.Add(new ConsumableType { Name = "  padded", StockMode = ConsumableStockModes.Undated, DisplayOrder = 1 });
            await raw.SaveChangesAsync();
        });
    }

    // 9.2: a configured type behaves like the initial ones with no code change.
    [Fact]
    public async Task Printer_heads_and_a_new_type_get_a_panel_counts_and_stock()
    {
        await ResetAsync();
        await using var context = _database.CreateContext();
        var configuration = Configuration(context);
        var inventory = Inventory(context);

        Assert.True((await configuration.AddConsumableAsync(InitialTypes.PrinterHeads, "Head PK/MK", "HEAD-1", null, CancellationToken.None)).Completed);
        var head = (await inventory.LoadAsync(CancellationToken.None)).Rows(InitialTypes.PrinterHeads).Single();
        Assert.True(head.HasExpirationDate);
        Assert.Equal("Enter the expiration date.", (await inventory.AddStockAsync(head.PrintingConsumableId, 1, null, CancellationToken.None)).Message);
        var date = DateOnly.FromDateTime(DateTime.Today).AddMonths(18);
        Assert.True((await inventory.AddStockAsync(head.PrintingConsumableId, 2, date, CancellationToken.None)).Completed);
        Assert.Contains("Acknowledge that", (await inventory.RemoveStockAsync(head.PrintingConsumableId, 1, false, null, CancellationToken.None)).Message);
        Assert.True((await inventory.RemoveStockAsync(head.PrintingConsumableId, 1, true, date, CancellationToken.None)).Completed);

        Assert.True((await configuration.AddConsumableTypeAsync("Cleaning kits", ConsumableStockModes.Undated, false, false, 0, CancellationToken.None)).Completed);
        Assert.True((await configuration.AddConsumableAsync("Cleaning kits", "Kit A", "KIT-A", null, CancellationToken.None)).Completed);
        var view = await inventory.LoadAsync(CancellationToken.None);
        Assert.Equal("Cleaning kits", view.Types[0].Name);
        var kit = view.Rows("Cleaning kits").Single();
        Assert.False(kit.HasExpirationDate);
        Assert.Equal("This material does not use an expiration date.", (await inventory.AddStockAsync(kit.PrintingConsumableId, 1, date, CancellationToken.None)).Message);
        var alerts = await inventory.CriticalAlertsAsync(CancellationToken.None);
        Assert.Equal(1, alerts.NeedsReorder.Count("Cleaning kits"));
        Assert.Equal(view.Types.Select(type => type.Name), alerts.NeedsReorder.ByType.Select(type => type.Name));
        Assert.Equal("This type does not use printer compatibility.", (await configuration.SetCompatibilityAsync(await AnyPrinterAsync(configuration), kit.PrintingConsumableId, true, CancellationToken.None)).Message);
    }

    // 4.5(c), (e), (f), 4.6, 4.7(c) and 9.3.
    [Fact]
    public async Task Type_rules_refuse_and_change_nothing()
    {
        await ResetAsync();
        await using var context = _database.CreateContext();
        var configuration = Configuration(context);
        var inventory = Inventory(context);
        Assert.True((await configuration.AddConsumableTypeAsync("Rolls", ConsumableStockModes.DateOptional, false, true, 5, CancellationToken.None)).Completed);
        var rolls = await configuration.TypeIdAsync("Rolls");
        Assert.True((await configuration.AddConsumableAsync("Rolls", "Roll A", "ROLL-A", null, CancellationToken.None)).Completed);
        var roll = (await inventory.LoadAsync(CancellationToken.None)).Rows("Rolls").Single();
        var date = DateOnly.FromDateTime(DateTime.Today).AddMonths(10);
        Assert.True((await inventory.AddStockAsync(roll.PrintingConsumableId, 3, date, CancellationToken.None)).Completed);

        Assert.Equal("This type has stock that does not fit that rule.", (await configuration.UpdateConsumableTypeAsync(rolls, "Rolls", ConsumableStockModes.Undated, false, true, 5, CancellationToken.None)).Message);
        Assert.True((await configuration.UpdateConsumableTypeAsync(rolls, "Rolls", ConsumableStockModes.DateRequired, false, true, 5, CancellationToken.None)).Completed);
        Assert.Equal("This type is used by materials. Deactivate it instead.", (await configuration.DeleteConsumableTypeAsync(rolls, CancellationToken.None)).Message);
        Assert.Equal("This material has stock, so its category stays the same.", (await configuration.UpdateConsumableAsync(roll.PrintingConsumableId, await configuration.TypeIdAsync(InitialTypes.Paper), "Roll A", "ROLL-A", null, 0, 0, CancellationToken.None)).Message);

        Assert.Equal("This type still has active materials or stock.", (await configuration.SetConsumableTypeActiveAsync(rolls, false, CancellationToken.None)).Message);
        Assert.True((await configuration.SetConsumableActiveAsync(roll.PrintingConsumableId, false, CancellationToken.None)).Completed);
        Assert.Equal("This type still has active materials or stock.", (await configuration.SetConsumableTypeActiveAsync(rolls, false, CancellationToken.None)).Message);

        Assert.True((await configuration.SetConsumableActiveAsync(roll.PrintingConsumableId, true, CancellationToken.None)).Completed);
        var lots = await inventory.LoadLotsAsync(roll.PrintingConsumableId, CancellationToken.None);
        Assert.True((await inventory.AdjustLotAsync(roll.PrintingConsumableId, date, -3, "Counted zero", InventoryTestSupport.Technician, CancellationToken.None)).Completed);
        Assert.True((await configuration.SetConsumableActiveAsync(roll.PrintingConsumableId, false, CancellationToken.None)).Completed);
        Assert.True((await configuration.SetConsumableTypeActiveAsync(rolls, false, CancellationToken.None)).Completed);

        Assert.Equal("Activate the type of this material first.", (await configuration.SetConsumableActiveAsync(roll.PrintingConsumableId, true, CancellationToken.None)).Message);
        Assert.Equal("Choose a type.", (await configuration.AddConsumableAsync(rolls, "Roll B", null, null, CancellationToken.None)).Message);
        Assert.DoesNotContain((await inventory.LoadAsync(CancellationToken.None)).Types, type => type.ConsumableTypeId == rolls);
        Assert.Equal("This material has stock history. Deactivate it instead.", (await configuration.DeleteConsumableAsync(roll.PrintingConsumableId, CancellationToken.None)).Message);
        Assert.NotNull(lots);
    }

    // 6.2 to 6.5, 7.3, 7.6 and 9.4 to 9.6.
    [Fact]
    public async Task Lot_operations_write_the_register_and_keep_consumption_apart_from_corrections()
    {
        await ResetAsync();
        await using var context = _database.CreateContext();
        var configuration = Configuration(context);
        var inventory = Inventory(context);
        Assert.True((await configuration.AddConsumableAsync(InitialTypes.Cartridge, "Lot cartridge", "LOT-1", null, CancellationToken.None)).Completed);
        var id = (await inventory.LoadAsync(CancellationToken.None)).Rows(InitialTypes.Cartridge).Single(row => row.Code == "LOT-1").PrintingConsumableId;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var old = today.AddMonths(2);
        var newer = today.AddMonths(9);
        Assert.True((await inventory.AddStockAsync(id, 2, old, CancellationToken.None)).Completed);
        Assert.True((await inventory.AddStockAsync(id, 4, newer, CancellationToken.None)).Completed);

        var lots = await inventory.LoadLotsAsync(id, CancellationToken.None);
        Assert.NotNull(lots);
        Assert.Equal([old, newer], lots.Lots.Select(lot => lot.ExpirationDate).ToArray());
        Assert.True(lots.Lots[0].Oldest);
        Assert.False(lots.Lots[1].Oldest);

        var needsConfirmation = await inventory.RemoveFromLotAsync(id, newer, 1, false, InventoryTestSupport.Technician, CancellationToken.None);
        Assert.False(needsConfirmation.Completed);
        Assert.True(needsConfirmation.NeedsOutOfOrderConfirmation);
        Assert.True((await inventory.RemoveFromLotAsync(id, newer, 1, true, InventoryTestSupport.Technician, CancellationToken.None)).Completed);
        Assert.Equal("That lot changed. Open it again.", (await inventory.RemoveFromLotAsync(id, old, 3, false, InventoryTestSupport.Technician, CancellationToken.None)).Message);
        Assert.Equal("That lot changed. Open it again.", (await inventory.RemoveFromLotAsync(id, today.AddYears(5), 1, true, InventoryTestSupport.Technician, CancellationToken.None)).Message);

        Assert.Equal("Enter the reason for the adjustment.", (await inventory.AdjustLotAsync(id, newer, -1, "  ", InventoryTestSupport.Technician, CancellationToken.None)).Message);
        Assert.True((await inventory.AdjustLotAsync(id, newer, -2, "Two were broken in the box", InventoryTestSupport.Technician, CancellationToken.None)).Completed);
        Assert.True((await inventory.AdjustLotAsync(id, newer, 1, "Counted one more", InventoryTestSupport.Technician, CancellationToken.None)).Completed);
        var missing = today.AddMonths(12);
        Assert.True((await inventory.AdjustLotAsync(id, missing, 5, "Box found in storage", InventoryTestSupport.Technician, CancellationToken.None)).Completed);
        Assert.Equal("Enter the expiration date.", (await inventory.AdjustLotAsync(id, null, 1, "No date", InventoryTestSupport.Technician, CancellationToken.None)).Message);
        Assert.True((await inventory.RemoveStockAsync(id, 3, true, old, CancellationToken.None)).Completed);
        Assert.Equal("The signed-in identity could not be verified.", (await inventory.AddStockAsync(id, 1, old, null, CancellationToken.None)).Message);

        await using var check = _database.CreateContext();
        var movements = await check.PrintingStockMovements.AsNoTracking()
            .Where(movement => movement.PrintingConsumableId == id)
            .OrderBy(movement => movement.PrintingStockMovementId)
            .ToListAsync();
        Assert.Equal(
            [StockMovementTypes.Received, StockMovementTypes.Received, StockMovementTypes.Used, StockMovementTypes.Adjustment, StockMovementTypes.Adjustment, StockMovementTypes.Adjustment, StockMovementTypes.Used, StockMovementTypes.Used],
            movements.Select(movement => movement.MovementType).ToArray());
        Assert.Equal([2, 4, -1, -2, 1, 5, -2, -1], movements.Select(movement => movement.QuantityChange).ToArray());
        Assert.Equal([2, 4, 3, 1, 2, 5, 0, 1], movements.Select(movement => movement.LotQuantityAfter).ToArray());
        Assert.True(movements[2].OutOfOrder);
        Assert.All(movements.Where(movement => movement.MovementType != StockMovementTypes.Adjustment), movement => Assert.Null(movement.Reason));
        Assert.Equal("Two were broken in the box", movements[3].Reason);
        Assert.Equal(movements[6].OperationId, movements[7].OperationId);
        Assert.Equal(old, movements[6].ExpirationDate);
        Assert.Equal(newer, movements[7].ExpirationDate);
        Assert.All(movements, movement => Assert.Equal(InventoryTestSupport.Technician.ObjectId, movement.PerformedByObjectId));
        Assert.All(movements, movement => Assert.Equal("tech@example.edu", movement.PerformedByAccountName));

        var finalLots = await check.PrintingStockEntries.AsNoTracking().Where(entry => entry.PrintingConsumableId == id && entry.Quantity > 0).ToListAsync();
        Assert.Equal(1, finalLots.Where(entry => entry.ExpirationDate == newer).Sum(entry => entry.Quantity));
        Assert.Equal(5, finalLots.Where(entry => entry.ExpirationDate == missing).Sum(entry => entry.Quantity));
        Assert.DoesNotContain(finalLots, entry => entry.ExpirationDate == old);

        await Assert.ThrowsAnyAsync<DbUpdateException>(async () =>
        {
            check.PrintingStockMovements.Add(new PrintingStockMovement
            {
                OperationId = Guid.NewGuid(),
                PrintingConsumableId = id,
                MovementType = StockMovementTypes.Adjustment,
                QuantityChange = 1,
                LotQuantityAfter = 1,
                OccurredAt = DateTime.UtcNow,
                PerformedByObjectId = Guid.NewGuid()
            });
            await check.SaveChangesAsync();
        });
    }

    // 9.6: concurrent consumption of one lot never goes below zero and the register matches the final quantity.
    [Fact]
    public async Task Concurrent_lot_removals_match_the_register()
    {
        await ResetAsync();
        int id;
        var date = DateOnly.FromDateTime(DateTime.Today).AddMonths(6);
        await using (var setup = _database.CreateContext())
        {
            Assert.True((await Configuration(setup).AddConsumableAsync(InitialTypes.Laminating, "Race film", "RACE-FILM", null, CancellationToken.None)).Completed);
            id = (await Inventory(setup).LoadAsync(CancellationToken.None)).Rows(InitialTypes.Laminating).Single(row => row.Code == "RACE-FILM").PrintingConsumableId;
            Assert.True((await Inventory(setup).AddStockAsync(id, 5, date, CancellationToken.None)).Completed);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var context = _database.CreateContext();
            return await Inventory(context).RemoveFromLotAsync(id, date, 2, false, InventoryTestSupport.Technician, CancellationToken.None);
        }));

        await using var check = _database.CreateContext();
        var remaining = await check.PrintingStockEntries.Where(entry => entry.PrintingConsumableId == id).SumAsync(entry => entry.Quantity);
        var used = await check.PrintingStockMovements.Where(movement => movement.PrintingConsumableId == id && movement.MovementType == StockMovementTypes.Used).SumAsync(movement => -movement.QuantityChange);
        Assert.True(remaining >= 0);
        Assert.Equal(5 - remaining, used);
        Assert.Equal(results.Count(result => result.Completed) * 2, used);
    }

    private static OperatorConfiguration Configuration(PosterPrintRequest.Infrastructure.Persistence.PosterPrintRequestDbContext context) =>
        new(context, NullLogger<OperatorConfiguration>.Instance);

    private static PrintingInventory Inventory(PosterPrintRequest.Infrastructure.Persistence.PosterPrintRequestDbContext context) =>
        new(context, NullLogger<PrintingInventory>.Instance);

    private static async Task<int> AnyPrinterAsync(OperatorConfiguration configuration)
    {
        var name = "Printer " + Guid.NewGuid().ToString("N")[..8];
        Assert.True((await configuration.AddPrinterModelAsync(name, CancellationToken.None)).Completed);
        return (await configuration.LoadAsync(CancellationToken.None)).PrinterModels.Single(model => model.Name == name).Id;
    }

    private async Task ResetAsync()
    {
        await using var context = _database.CreateContext();
        await context.PrinterModelConsumables.ExecuteDeleteAsync();
        await context.PrintingStockMovements.ExecuteDeleteAsync();
        await context.PrintingStockEntries.ExecuteDeleteAsync();
        await context.PrintingConsumables.ExecuteDeleteAsync();
        await InventoryTestSupport.ResetTypesAsync(context);
    }
}
