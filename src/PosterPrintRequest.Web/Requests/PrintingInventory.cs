using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web.Requests;

public sealed class ConsumableRow
{
    public required int PrintingConsumableId { get; init; }

    public required int ConsumableTypeId { get; init; }

    public required string TypeName { get; init; }

    public required string StockMode { get; init; }

    public required bool AcknowledgeEarliestExpiration { get; init; }

    public required string Name { get; init; }

    public string? Code { get; init; }

    public string? Capacity { get; init; }

    public required bool HasExpirationDate { get; init; }

    public required int CurrentQuantity { get; init; }

    public required int LowStockThreshold { get; init; }

    public required int CriticalStockThreshold { get; init; }

    public required string Status { get; init; }

    public DateOnly? EarliestExpirationDate { get; init; }

    public string? EarliestExpirationAlert { get; init; }

    public required IReadOnlyList<StockEntryRow> Entries { get; init; }
}

public sealed class StockEntryRow
{
    public required int PrintingStockEntryId { get; init; }

    public required int Quantity { get; init; }

    public DateOnly? ExpirationDate { get; init; }

    public string? ExpirationAlert { get; init; }
}

/// <summary>One inventory panel: an active consumable type and its active consumables (contract section 4.8).</summary>
public sealed class InventoryTypePanel
{
    public required int ConsumableTypeId { get; init; }

    public required string Name { get; init; }

    public required string StockMode { get; init; }

    public required IReadOnlyList<ConsumableRow> Rows { get; init; }
}

public sealed class PrintingInventoryView
{
    public required IReadOnlyList<InventoryTypePanel> Types { get; init; }

    public IEnumerable<ConsumableRow> AllRows => Types.SelectMany(type => type.Rows);
}

public sealed class ConsumableAlert
{
    public required int PrintingConsumableId { get; init; }

    public required int ConsumableTypeId { get; init; }

    public required string TypeName { get; init; }

    public required string Name { get; init; }

    public string? Code { get; init; }

    public required int CurrentQuantity { get; init; }

    public required string Status { get; init; }

    public DateOnly? ExpirationDate { get; init; }

    public required string Detail { get; init; }
}

public sealed class SupplyTypeCount
{
    public required int ConsumableTypeId { get; init; }

    public required string Name { get; init; }

    public required int Count { get; init; }
}

/// <summary>Dashboard counts split by active consumable type, in the order set in Configuration (contract section 4.8).</summary>
public sealed class SupplyCategorySummary
{
    public required IReadOnlyList<SupplyTypeCount> ByType { get; init; }

    public int Total => ByType.Sum(type => type.Count);
}

public sealed class ConsumableAttention
{
    public required IReadOnlyList<ConsumableAlert> Depleted { get; init; }

    public required IReadOnlyList<ConsumableAlert> LowStock { get; init; }

    public required IReadOnlyList<ConsumableAlert> ExpiringSoon { get; init; }

    public required SupplyCategorySummary NeedsReorder { get; init; }

    public required SupplyCategorySummary ExpiringSoonSummary { get; init; }
}

public sealed class InventoryOutcome
{
    public bool Completed { get; init; }

    public string? Message { get; init; }

    /// <summary>Set when a lot removal needs the out-of-order confirmation (contract section 6.3).</summary>
    public bool NeedsOutOfOrderConfirmation { get; init; }

    public static InventoryOutcome Success() => new() { Completed = true };

    public static InventoryOutcome Failure(string message) => new() { Completed = false, Message = message };
}

/// <summary>One lot of a consumable in the lot dialog (contract section 6.2).</summary>
public sealed class LotRow
{
    public DateOnly? ExpirationDate { get; init; }

    public required int Quantity { get; init; }

    public string? ExpirationAlert { get; init; }

    public required bool Oldest { get; init; }
}

public sealed class StockMovementRow
{
    public required long PrintingStockMovementId { get; init; }

    public required DateTime OccurredAtUtc { get; init; }

    public DateOnly? ExpirationDate { get; init; }

    public required string MovementType { get; init; }

    public required int QuantityChange { get; init; }

    public required int LotQuantityAfter { get; init; }

    public required bool OutOfOrder { get; init; }

    public string? Reason { get; init; }

    public string? PerformedByAccountName { get; init; }
}

public sealed class ConsumableLots
{
    public required ConsumableRow Consumable { get; init; }

    public required IReadOnlyList<LotRow> Lots { get; init; }

    public required IReadOnlyList<StockMovementRow> Movements { get; init; }
}

public interface IPrintingInventory
{
    Task<PrintingInventoryView> LoadAsync(CancellationToken cancellationToken);

    Task<ConsumableAttention> CriticalAlertsAsync(CancellationToken cancellationToken);

    Task<InventoryOutcome> AddStockAsync(int printingConsumableId, int quantity, DateOnly? expirationDate, InstitutionalActor? actor, CancellationToken cancellationToken);

    Task<InventoryOutcome> RemoveStockAsync(
        int printingConsumableId,
        int quantity,
        bool earliestExpirationAcknowledged,
        DateOnly? acknowledgedExpirationDate,
        InstitutionalActor? actor,
        CancellationToken cancellationToken);

    Task<ConsumableLots?> LoadLotsAsync(int printingConsumableId, CancellationToken cancellationToken);

    Task<InventoryOutcome> RemoveFromLotAsync(
        int printingConsumableId,
        DateOnly? expirationDate,
        int quantity,
        bool outOfOrderConfirmed,
        InstitutionalActor? actor,
        CancellationToken cancellationToken);

    Task<InventoryOutcome> AdjustLotAsync(
        int printingConsumableId,
        DateOnly? expirationDate,
        int quantityChange,
        string? reason,
        InstitutionalActor? actor,
        CancellationToken cancellationToken);
}

/// <summary>
/// The only writer of PrintingStockEntries. Every stock change writes its PrintingStockMovements rows in the same
/// transaction (Consumables Inventory Contract sections 6 and 7).
/// </summary>
public sealed class PrintingInventory : IPrintingInventory
{
    public const int MovementsShown = 200;

    public const int ReasonMaxLength = 500;

    private const string LotChanged = "That lot changed. Open it again.";

    private readonly PosterPrintRequestDbContext _db;
    private readonly ILogger<PrintingInventory> _logger;
    private readonly TimeProvider _time;

    public PrintingInventory(PosterPrintRequestDbContext db, ILogger<PrintingInventory> logger, TimeProvider? time = null)
    {
        _db = db;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<PrintingInventoryView> LoadAsync(CancellationToken cancellationToken)
    {
        var types = await _db.ConsumableTypes
            .Where(type => type.Active)
            .OrderBy(type => type.DisplayOrder)
            .ThenBy(type => type.Name)
            .ToListAsync(cancellationToken);
        var items = await _db.PrintingConsumables
            .Include(item => item.StockEntries)
            .Include(item => item.ConsumableType)
            .Where(item => item.Active && item.ConsumableType.Active)
            .ToListAsync(cancellationToken);
        if (ApplyDerivedStatus(items))
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        var today = Today();
        return new PrintingInventoryView
        {
            Types = types
                .Select(type => new InventoryTypePanel
                {
                    ConsumableTypeId = type.ConsumableTypeId,
                    Name = type.Name,
                    StockMode = type.StockMode,
                    Rows = items
                        .Where(item => item.ConsumableTypeId == type.ConsumableTypeId)
                        .OrderBy(item => item.Name, StringComparer.Ordinal)
                        .ThenBy(item => item.Code ?? string.Empty, StringComparer.Ordinal)
                        .Select(item => ToRow(item, today))
                        .ToList()
                })
                .ToList()
        };
    }

    public async Task<ConsumableAttention> CriticalAlertsAsync(CancellationToken cancellationToken)
    {
        var view = await LoadAsync(cancellationToken);
        var rows = view.AllRows.ToList();
        var depleted = new List<ConsumableAlert>();
        var lowStock = new List<ConsumableAlert>();
        foreach (var row in rows)
        {
            if (row.Status == ConsumableStock.Depleted)
            {
                depleted.Add(Alert(row, "Depleted, quantity " + row.CurrentQuantity.ToString(CultureInfo.InvariantCulture)));
            }
            else if (row.Status == ConsumableStock.LowStock)
            {
                lowStock.Add(Alert(row, "Low stock, quantity " + row.CurrentQuantity.ToString(CultureInfo.InvariantCulture)));
            }
        }

        var expiringSoon = new List<ConsumableAlert>();
        foreach (var row in rows)
        {
            var upcoming = row.Entries.FirstOrDefault(entry =>
                entry.ExpirationAlert is ConsumableStock.ExpiresWithinSixMonths or ConsumableStock.ExpiresWithinTwelveMonths);
            if (upcoming?.ExpirationDate is DateOnly expirationDate && upcoming.ExpirationAlert is not null)
            {
                expiringSoon.Add(Alert(
                    row,
                    upcoming.ExpirationAlert + ", " + expirationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    expirationDate));
            }
        }

        return new ConsumableAttention
        {
            Depleted = depleted,
            LowStock = lowStock,
            ExpiringSoon = expiringSoon,
            NeedsReorder = Summarize(view, depleted.Concat(lowStock)),
            ExpiringSoonSummary = Summarize(view, expiringSoon)
        };
    }

    public async Task<ConsumableLots?> LoadLotsAsync(int printingConsumableId, CancellationToken cancellationToken)
    {
        var item = await FindActiveAsync(printingConsumableId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var today = Today();
        var lots = Lots(item);
        var movements = await _db.PrintingStockMovements
            .AsNoTracking()
            .Where(movement => movement.PrintingConsumableId == printingConsumableId)
            .OrderByDescending(movement => movement.OccurredAt)
            .ThenByDescending(movement => movement.PrintingStockMovementId)
            .Take(MovementsShown)
            .Select(movement => new StockMovementRow
            {
                PrintingStockMovementId = movement.PrintingStockMovementId,
                OccurredAtUtc = movement.OccurredAt,
                ExpirationDate = movement.ExpirationDate,
                MovementType = movement.MovementType,
                QuantityChange = movement.QuantityChange,
                LotQuantityAfter = movement.LotQuantityAfter,
                OutOfOrder = movement.OutOfOrder,
                Reason = movement.Reason,
                PerformedByAccountName = movement.PerformedByAccountName
            })
            .ToListAsync(cancellationToken);
        return new ConsumableLots
        {
            Consumable = ToRow(item, today),
            Lots = lots
                .Select((lot, index) => new LotRow
                {
                    ExpirationDate = lot.Date,
                    Quantity = lot.Quantity,
                    ExpirationAlert = ConsumableStock.ExpirationAlert(lot.Date, today),
                    Oldest = index == 0
                })
                .ToList(),
            Movements = movements
        };
    }

    public Task<InventoryOutcome> AddStockAsync(
        int printingConsumableId,
        int quantity,
        DateOnly? expirationDate,
        InstitutionalActor? actor,
        CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(actor, (performer, token) => AddStockCoreAsync(printingConsumableId, quantity, expirationDate, performer, token), cancellationToken);

    public Task<InventoryOutcome> RemoveStockAsync(
        int printingConsumableId,
        int quantity,
        bool earliestExpirationAcknowledged,
        DateOnly? acknowledgedExpirationDate,
        InstitutionalActor? actor,
        CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(
            actor,
            (performer, token) => RemoveStockCoreAsync(printingConsumableId, quantity, earliestExpirationAcknowledged, acknowledgedExpirationDate, performer, token),
            cancellationToken);

    public Task<InventoryOutcome> RemoveFromLotAsync(
        int printingConsumableId,
        DateOnly? expirationDate,
        int quantity,
        bool outOfOrderConfirmed,
        InstitutionalActor? actor,
        CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(
            actor,
            (performer, token) => RemoveFromLotCoreAsync(printingConsumableId, expirationDate, quantity, outOfOrderConfirmed, performer, token),
            cancellationToken);

    public Task<InventoryOutcome> AdjustLotAsync(
        int printingConsumableId,
        DateOnly? expirationDate,
        int quantityChange,
        string? reason,
        InstitutionalActor? actor,
        CancellationToken cancellationToken) =>
        ExecuteExclusiveAsync(
            actor,
            (performer, token) => AdjustLotCoreAsync(printingConsumableId, expirationDate, quantityChange, reason, performer, token),
            cancellationToken);

    /// <summary>Contract section 6.4: trimmed, 1 to 500 characters, no control characters.</summary>
    public static string? CheckReason(string? reason)
    {
        var text = reason?.Trim() ?? "";
        if (text.Length == 0)
        {
            return "Enter the reason for the adjustment.";
        }

        if (text.Length > ReasonMaxLength)
        {
            return $"Use {ReasonMaxLength} characters or fewer.";
        }

        return text.Any(char.IsControl) ? "Write the reason on one line, without line breaks or tabs." : null;
    }

    private async Task<InventoryOutcome> AddStockCoreAsync(
        int printingConsumableId,
        int quantity,
        DateOnly? expirationDate,
        Operation operation,
        CancellationToken cancellationToken)
    {
        var item = await FindActiveAsync(printingConsumableId, cancellationToken);
        if (item is null)
        {
            return InventoryOutcome.Failure("That consumable is not in the inventory.");
        }

        if (quantity <= 0)
        {
            return InventoryOutcome.Failure("Enter a quantity greater than zero.");
        }

        var current = TotalQuantity(item);
        if (current > int.MaxValue - quantity)
        {
            return InventoryOutcome.Failure("Enter a smaller quantity.");
        }

        var dateError = CheckDate(item.ConsumableType, expirationDate);
        if (dateError is not null)
        {
            return InventoryOutcome.Failure(dateError);
        }

        var after = Increase(item, expirationDate, quantity);
        Record(item, expirationDate, StockMovementTypes.Received, quantity, after, outOfOrder: false, reason: null, operation);
        await SaveAsync(item, cancellationToken);
        _logger.LogInformation(
            "Technician added {Quantity} of {Consumable} expiring {ExpirationDate}. Quantity is now {CurrentQuantity}.",
            quantity,
            item.Code ?? item.Name,
            FormatDate(expirationDate),
            current + quantity);
        return InventoryOutcome.Success();
    }

    private async Task<InventoryOutcome> RemoveStockCoreAsync(
        int printingConsumableId,
        int quantity,
        bool earliestExpirationAcknowledged,
        DateOnly? acknowledgedExpirationDate,
        Operation operation,
        CancellationToken cancellationToken)
    {
        var item = await FindActiveAsync(printingConsumableId, cancellationToken);
        if (item is null)
        {
            return InventoryOutcome.Failure("That consumable is not in the inventory.");
        }

        if (quantity <= 0)
        {
            return InventoryOutcome.Failure("Enter a quantity greater than zero.");
        }

        var ordered = RemovalOrder(item);
        if (item.ConsumableType.AcknowledgeEarliestExpiration && ordered.Count > 0)
        {
            var earliest = ordered[0].ExpirationDate;
            if (!earliestExpirationAcknowledged || acknowledgedExpirationDate != earliest)
            {
                return InventoryOutcome.Failure(
                    "Acknowledge that " + FormatDate(earliest) + " is the first expiration before removing this material.");
            }
        }

        if (quantity > TotalQuantity(item))
        {
            return InventoryOutcome.Failure("That removal would reduce the inventory below zero.");
        }

        // Section 7.3: one Used row per lot, earliest first, with the same OperationId.
        var remaining = quantity;
        var taken = new List<(DateOnly? Date, int Quantity)>();
        foreach (var entry in ordered)
        {
            var take = Math.Min(entry.Quantity, remaining);
            entry.Quantity -= take;
            remaining -= take;
            if (taken.Count > 0 && taken[^1].Date == entry.ExpirationDate)
            {
                taken[^1] = (entry.ExpirationDate, taken[^1].Quantity + take);
            }
            else
            {
                taken.Add((entry.ExpirationDate, take));
            }

            if (remaining == 0)
            {
                break;
            }
        }

        foreach (var (date, amount) in taken)
        {
            Record(item, date, StockMovementTypes.Used, -amount, LotQuantity(item, date), outOfOrder: false, reason: null, operation);
        }

        RemoveEmpty(item);
        await SaveAsync(item, cancellationToken);
        _logger.LogInformation(
            "Technician removed {Quantity} of {Consumable}. Quantity is now {CurrentQuantity}.",
            quantity,
            item.Code ?? item.Name,
            TotalQuantity(item));
        return InventoryOutcome.Success();
    }

    private async Task<InventoryOutcome> RemoveFromLotCoreAsync(
        int printingConsumableId,
        DateOnly? expirationDate,
        int quantity,
        bool outOfOrderConfirmed,
        Operation operation,
        CancellationToken cancellationToken)
    {
        var item = await FindActiveAsync(printingConsumableId, cancellationToken);
        if (item is null)
        {
            return InventoryOutcome.Failure("That consumable is not in the inventory.");
        }

        if (quantity <= 0)
        {
            return InventoryOutcome.Failure("Enter a quantity greater than zero.");
        }

        // Section 6.6: the lot is read again inside the transaction.
        var lots = Lots(item);
        var index = lots.FindIndex(lot => lot.Date == expirationDate);
        if (index < 0 || lots[index].Quantity < quantity)
        {
            return InventoryOutcome.Failure(LotChanged);
        }

        var outOfOrder = index > 0;
        if (outOfOrder && !outOfOrderConfirmed)
        {
            return new InventoryOutcome
            {
                Completed = false,
                NeedsOutOfOrderConfirmation = true,
                Message = "An older lot exists (" + FormatDate(lots[0].Date) + "). Confirm to remove from this lot."
            };
        }

        Decrease(item, expirationDate, quantity);
        Record(item, expirationDate, StockMovementTypes.Used, -quantity, LotQuantity(item, expirationDate), outOfOrder, reason: null, operation);
        RemoveEmpty(item);
        await SaveAsync(item, cancellationToken);
        _logger.LogInformation(
            "Technician removed {Quantity} of {Consumable} from lot {ExpirationDate}. Out of order: {OutOfOrder}.",
            quantity,
            item.Code ?? item.Name,
            FormatDate(expirationDate),
            outOfOrder);
        return InventoryOutcome.Success();
    }

    private async Task<InventoryOutcome> AdjustLotCoreAsync(
        int printingConsumableId,
        DateOnly? expirationDate,
        int quantityChange,
        string? reason,
        Operation operation,
        CancellationToken cancellationToken)
    {
        var item = await FindActiveAsync(printingConsumableId, cancellationToken);
        if (item is null)
        {
            return InventoryOutcome.Failure("That consumable is not in the inventory.");
        }

        if (quantityChange == 0)
        {
            return InventoryOutcome.Failure("Enter a quantity greater than zero.");
        }

        var reasonError = CheckReason(reason);
        if (reasonError is not null)
        {
            return InventoryOutcome.Failure(reasonError);
        }

        var lotQuantity = LotQuantity(item, expirationDate);
        if (quantityChange < 0)
        {
            if (lotQuantity == 0 || lotQuantity < -quantityChange)
            {
                return InventoryOutcome.Failure(LotChanged);
            }

            Decrease(item, expirationDate, -quantityChange);
        }
        else
        {
            // Section 6.5: an adjustment that adds can create a lot, within the stock mode of the type.
            var dateError = CheckDate(item.ConsumableType, expirationDate);
            if (dateError is not null)
            {
                return InventoryOutcome.Failure(dateError);
            }

            if (TotalQuantity(item) > int.MaxValue - quantityChange)
            {
                return InventoryOutcome.Failure("Enter a smaller quantity.");
            }

            Increase(item, expirationDate, quantityChange);
        }

        Record(item, expirationDate, StockMovementTypes.Adjustment, quantityChange, LotQuantity(item, expirationDate), outOfOrder: false, reason!.Trim(), operation);
        RemoveEmpty(item);
        await SaveAsync(item, cancellationToken);
        _logger.LogInformation(
            "Technician adjusted {Consumable} lot {ExpirationDate} by {QuantityChange}.",
            item.Code ?? item.Name,
            FormatDate(expirationDate),
            quantityChange);
        return InventoryOutcome.Success();
    }

    private static string? CheckDate(ConsumableType type, DateOnly? expirationDate) =>
        type.StockMode switch
        {
            ConsumableStockModes.Undated when expirationDate is not null => "This material does not use an expiration date.",
            ConsumableStockModes.DateRequired when expirationDate is null => "Enter the expiration date.",
            _ => null
        };

    /// <summary>Same rule as Add stock: the entry with that date and a positive quantity, smallest id first, or a new entry.</summary>
    private static int Increase(PrintingConsumable item, DateOnly? expirationDate, int quantity)
    {
        var entry = item.StockEntries
            .Where(candidate => candidate.Quantity > 0 && candidate.ExpirationDate == expirationDate)
            .OrderBy(candidate => candidate.PrintingStockEntryId)
            .FirstOrDefault();
        if (entry is null)
        {
            item.StockEntries.Add(new PrintingStockEntry { Quantity = quantity, ExpirationDate = expirationDate });
        }
        else
        {
            entry.Quantity += quantity;
        }

        return LotQuantity(item, expirationDate);
    }

    private static void Decrease(PrintingConsumable item, DateOnly? expirationDate, int quantity)
    {
        var remaining = quantity;
        foreach (var entry in item.StockEntries
                     .Where(candidate => candidate.Quantity > 0 && candidate.ExpirationDate == expirationDate)
                     .OrderBy(candidate => candidate.PrintingStockEntryId))
        {
            var take = Math.Min(entry.Quantity, remaining);
            entry.Quantity -= take;
            remaining -= take;
            if (remaining == 0)
            {
                break;
            }
        }
    }

    private void RemoveEmpty(PrintingConsumable item)
    {
        // A lot that reaches zero is deleted, as today (contract section 6.7). Its movements stay.
        foreach (var entry in item.StockEntries.Where(entry => entry.Quantity == 0).ToList())
        {
            _db.Remove(entry);
        }
    }

    private void Record(
        PrintingConsumable item,
        DateOnly? expirationDate,
        string movementType,
        int quantityChange,
        int lotQuantityAfter,
        bool outOfOrder,
        string? reason,
        Operation operation)
    {
        _db.PrintingStockMovements.Add(new PrintingStockMovement
        {
            OperationId = operation.Id,
            PrintingConsumableId = item.PrintingConsumableId,
            ExpirationDate = expirationDate,
            MovementType = movementType,
            QuantityChange = quantityChange,
            LotQuantityAfter = lotQuantityAfter,
            OutOfOrder = outOfOrder,
            Reason = reason,
            OccurredAt = operation.OccurredAtUtc,
            PerformedByObjectId = operation.Actor.ObjectId,
            PerformedByAccountName = operation.Actor.PreferredUsername
        });
    }

    private async Task SaveAsync(PrintingConsumable item, CancellationToken cancellationToken)
    {
        item.Status = ConsumableStock.FromQuantity(TotalQuantity(item), item.LowStockThreshold, item.CriticalStockThreshold);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Section 6.6 and 7.4: one serializable transaction per operation, with the existing deadlock retry. The stock
    /// change and its movement rows are committed together or not at all. Section 7.6: without a valid actor nothing is written.
    /// </summary>
    private async Task<InventoryOutcome> ExecuteExclusiveAsync(
        InstitutionalActor? actor,
        Func<Operation, CancellationToken, Task<InventoryOutcome>> action,
        CancellationToken cancellationToken)
    {
        if (actor is null || !actor.IsValid)
        {
            return InventoryOutcome.Failure("The signed-in identity could not be verified.");
        }

        const int attempts = 3;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var operation = new Operation(Guid.NewGuid(), _time.GetUtcNow().UtcDateTime, actor);
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                var outcome = await action(operation, cancellationToken);
                if (!outcome.Completed)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _db.ChangeTracker.Clear();
                    return outcome;
                }

                await transaction.CommitAsync(cancellationToken);
                return outcome;
            }
            catch (Exception exception) when (IsDeadlock(exception))
            {
                _db.ChangeTracker.Clear();
                if (attempt == attempts)
                {
                    return InventoryOutcome.Failure("The inventory changed while this update was saved. Enter it again.");
                }
            }
            catch
            {
                _db.ChangeTracker.Clear();
                throw;
            }
        }

        throw new InvalidOperationException("The inventory update did not finish.");
    }

    private static bool IsDeadlock(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number == 1205)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<PrintingConsumable?> FindActiveAsync(int printingConsumableId, CancellationToken cancellationToken) =>
        await _db.PrintingConsumables
            .Include(candidate => candidate.StockEntries)
            .Include(candidate => candidate.ConsumableType)
            .SingleOrDefaultAsync(
                candidate => candidate.PrintingConsumableId == printingConsumableId && candidate.Active && candidate.ConsumableType.Active,
                cancellationToken);

    private static ConsumableRow ToRow(PrintingConsumable item, DateOnly today)
    {
        var entries = RemovalOrder(item)
            .Select(entry => new StockEntryRow
            {
                PrintingStockEntryId = entry.PrintingStockEntryId,
                Quantity = entry.Quantity,
                ExpirationDate = entry.ExpirationDate,
                ExpirationAlert = ConsumableStock.ExpirationAlert(entry.ExpirationDate, today)
            })
            .ToList();
        var earliest = entries.FirstOrDefault(entry => entry.ExpirationDate is not null);
        return new ConsumableRow
        {
            PrintingConsumableId = item.PrintingConsumableId,
            ConsumableTypeId = item.ConsumableTypeId,
            TypeName = item.ConsumableType.Name,
            StockMode = item.ConsumableType.StockMode,
            AcknowledgeEarliestExpiration = item.ConsumableType.AcknowledgeEarliestExpiration,
            Name = item.Name,
            Code = item.Code,
            Capacity = item.Capacity,
            HasExpirationDate = item.HasExpirationDate,
            CurrentQuantity = entries.Sum(entry => entry.Quantity),
            LowStockThreshold = item.LowStockThreshold,
            CriticalStockThreshold = item.CriticalStockThreshold,
            Status = item.Status,
            EarliestExpirationDate = earliest?.ExpirationDate,
            EarliestExpirationAlert = earliest?.ExpirationAlert,
            Entries = entries
        };
    }

    /// <summary>Lots in the current removal order: earliest date first, the undated lot last (contract section 6.2).</summary>
    private static List<(DateOnly? Date, int Quantity)> Lots(PrintingConsumable item) =>
        RemovalOrder(item)
            .GroupBy(entry => entry.ExpirationDate)
            .Select(group => (group.Key, group.Sum(entry => entry.Quantity)))
            .ToList();

    private static List<PrintingStockEntry> RemovalOrder(PrintingConsumable item) =>
        item.StockEntries
            .Where(entry => entry.Quantity > 0)
            .OrderBy(entry => entry.ExpirationDate ?? DateOnly.MaxValue)
            .ThenBy(entry => entry.ExpirationDate is null ? 1 : 0)
            .ThenBy(entry => entry.PrintingStockEntryId)
            .ToList();

    private static int LotQuantity(PrintingConsumable item, DateOnly? expirationDate) =>
        item.StockEntries.Where(entry => entry.Quantity > 0 && entry.ExpirationDate == expirationDate).Sum(entry => entry.Quantity);

    private static int TotalQuantity(PrintingConsumable item) =>
        item.StockEntries.Where(entry => entry.Quantity > 0).Sum(entry => entry.Quantity);

    private static SupplyCategorySummary Summarize(PrintingInventoryView view, IEnumerable<ConsumableAlert> alerts)
    {
        var counted = alerts
            .GroupBy(alert => alert.PrintingConsumableId)
            .Select(group => group.First().ConsumableTypeId)
            .GroupBy(typeId => typeId)
            .ToDictionary(group => group.Key, group => group.Count());
        return new SupplyCategorySummary
        {
            ByType = view.Types
                .Select(type => new SupplyTypeCount
                {
                    ConsumableTypeId = type.ConsumableTypeId,
                    Name = type.Name,
                    Count = counted.TryGetValue(type.ConsumableTypeId, out var count) ? count : 0
                })
                .ToList()
        };
    }

    private static ConsumableAlert Alert(ConsumableRow row, string detail, DateOnly? expirationDate = null) => new()
    {
        PrintingConsumableId = row.PrintingConsumableId,
        ConsumableTypeId = row.ConsumableTypeId,
        TypeName = row.TypeName,
        Name = row.Name,
        Code = row.Code,
        CurrentQuantity = row.CurrentQuantity,
        Status = row.Status,
        ExpirationDate = expirationDate ?? row.EarliestExpirationDate,
        Detail = detail
    };

    private static bool ApplyDerivedStatus(IEnumerable<PrintingConsumable> items)
    {
        var changed = false;
        foreach (var item in items)
        {
            var status = ConsumableStock.FromQuantity(TotalQuantity(item), item.LowStockThreshold, item.CriticalStockThreshold);
            if (!string.Equals(item.Status, status, StringComparison.Ordinal))
            {
                item.Status = status;
                changed = true;
            }
        }

        return changed;
    }

    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime);

    private static string FormatDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none";

    private sealed record Operation(Guid Id, DateTime OccurredAtUtc, InstitutionalActor Actor);
}
