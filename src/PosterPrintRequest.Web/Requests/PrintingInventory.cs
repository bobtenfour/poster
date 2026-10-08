using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web.Requests;

public sealed class ConsumableRow
{
    public required int PrintingConsumableId { get; init; }

    public required string Category { get; init; }

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

public sealed class PrintingInventoryView
{
    public required IReadOnlyList<ConsumableRow> Cartridges { get; init; }

    public required IReadOnlyList<ConsumableRow> Paper { get; init; }

    public required IReadOnlyList<ConsumableRow> LaminatingMaterials { get; init; }
}

public sealed class ConsumableAlert
{
    public required int PrintingConsumableId { get; init; }

    public required string Category { get; init; }

    public required string Name { get; init; }

    public string? Code { get; init; }

    public required int CurrentQuantity { get; init; }

    public required string Status { get; init; }

    public DateOnly? ExpirationDate { get; init; }

    public required string Detail { get; init; }
}

public sealed class ConsumableAttention
{
    public required IReadOnlyList<ConsumableAlert> Depleted { get; init; }

    public required IReadOnlyList<ConsumableAlert> LowStock { get; init; }

    public required IReadOnlyList<ConsumableAlert> ExpiringSoon { get; init; }
}

public sealed class InventoryOutcome
{
    public bool Completed { get; init; }

    public string? Message { get; init; }

    public static InventoryOutcome Success() => new() { Completed = true };

    public static InventoryOutcome Failure(string message) => new() { Completed = false, Message = message };
}

public interface IPrintingInventory
{
    Task<PrintingInventoryView> LoadAsync(CancellationToken cancellationToken);

    Task<ConsumableAttention> CriticalAlertsAsync(CancellationToken cancellationToken);

    Task<InventoryOutcome> AddStockAsync(int printingConsumableId, int quantity, DateOnly? expirationDate, CancellationToken cancellationToken);

    Task<InventoryOutcome> RemoveStockAsync(
        int printingConsumableId,
        int quantity,
        bool earliestExpirationAcknowledged,
        DateOnly? acknowledgedExpirationDate,
        CancellationToken cancellationToken);
}

public sealed class PrintingInventory : IPrintingInventory
{
    private readonly PosterPrintRequestDbContext _db;
    private readonly ILogger<PrintingInventory> _logger;

    public PrintingInventory(PosterPrintRequestDbContext db, ILogger<PrintingInventory> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PrintingInventoryView> LoadAsync(CancellationToken cancellationToken)
    {
        var items = await _db.PrintingConsumables
            .Include(item => item.StockEntries)
            .Where(item => item.Active)
            .ToListAsync(cancellationToken);
        var changed = ApplyDerivedStatus(items);
        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        return new PrintingInventoryView
        {
            Cartridges = Rows(items, ConsumableCategory.Cartridge, today),
            Paper = Rows(items, ConsumableCategory.Paper, today),
            LaminatingMaterials = Rows(items, ConsumableCategory.Laminating, today)
        };
    }

    public async Task<ConsumableAttention> CriticalAlertsAsync(CancellationToken cancellationToken)
    {
        var view = await LoadAsync(cancellationToken);
        var rows = view.Cartridges.Concat(view.Paper).Concat(view.LaminatingMaterials).ToList();
        var depleted = new List<ConsumableAlert>();
        var lowStock = new List<ConsumableAlert>();
        foreach (var row in rows)
        {
            if (row.Status == ConsumableStock.Depleted)
            {
                depleted.Add(Alert(row, "Depleted, quantity " + row.CurrentQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)));
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
            ExpiringSoon = expiringSoon
        };
    }

    public async Task<InventoryOutcome> AddStockAsync(int printingConsumableId, int quantity, DateOnly? expirationDate, CancellationToken cancellationToken)
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

        if (item.Category == ConsumableCategory.Paper)
        {
            if (expirationDate is not null)
            {
                return InventoryOutcome.Failure("This material does not use an expiration date.");
            }
        }
        else if (item.Category == ConsumableCategory.Cartridge && expirationDate is null)
        {
            return InventoryOutcome.Failure("Enter the expiration date.");
        }

        var entry = item.StockEntries
            .Where(candidate => candidate.Quantity > 0 && candidate.ExpirationDate == expirationDate)
            .OrderBy(candidate => candidate.PrintingStockEntryId)
            .FirstOrDefault();
        if (entry is null)
        {
            item.StockEntries.Add(new PrintingStockEntry
            {
                Quantity = quantity,
                ExpirationDate = expirationDate
            });
        }
        else
        {
            entry.Quantity += quantity;
        }
        item.Status = ConsumableStock.FromQuantity(current + quantity, item.LowStockThreshold, item.CriticalStockThreshold);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Technician added {Quantity} of {Consumable} expiring {ExpirationDate}. Quantity is now {CurrentQuantity}.",
            quantity,
            item.Code ?? item.Name,
            expirationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none",
            current + quantity);
        return InventoryOutcome.Success();
    }

    public async Task<InventoryOutcome> RemoveStockAsync(
        int printingConsumableId,
        int quantity,
        bool earliestExpirationAcknowledged,
        DateOnly? acknowledgedExpirationDate,
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
        if (item.Category == ConsumableCategory.Cartridge && ordered.Count > 0)
        {
            var earliest = ordered[0].ExpirationDate;
            if (!earliestExpirationAcknowledged || acknowledgedExpirationDate != earliest)
            {
                var shown = earliest?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return InventoryOutcome.Failure(
                    "Acknowledge that " + shown + " is the first expiration before removing this cartridge.");
            }
        }

        if (quantity > TotalQuantity(item))
        {
            return InventoryOutcome.Failure("That removal would reduce the inventory below zero.");
        }

        var remaining = quantity;
        foreach (var entry in ordered)
        {
            var take = Math.Min(entry.Quantity, remaining);
            entry.Quantity -= take;
            remaining -= take;
            if (entry.Quantity == 0)
            {
                _db.Remove(entry);
            }

            if (remaining == 0)
            {
                break;
            }
        }

        var current = TotalQuantity(item);
        item.Status = ConsumableStock.FromQuantity(current, item.LowStockThreshold, item.CriticalStockThreshold);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Technician removed {Quantity} of {Consumable}. Quantity is now {CurrentQuantity}.",
            quantity,
            item.Code ?? item.Name,
            current);
        return InventoryOutcome.Success();
    }

    private static IReadOnlyList<ConsumableRow> Rows(IEnumerable<PrintingConsumable> items, string category, DateOnly today)
    {
        return items
            .Where(item => item.Category == category)
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Code ?? string.Empty, StringComparer.Ordinal)
            .Select(item => ToRow(item, today))
            .ToList();
    }

    private async Task<PrintingConsumable?> FindActiveAsync(int printingConsumableId, CancellationToken cancellationToken) =>
        await _db.PrintingConsumables
            .Include(candidate => candidate.StockEntries)
            .SingleOrDefaultAsync(
                candidate => candidate.PrintingConsumableId == printingConsumableId && candidate.Active,
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
            Category = item.Category,
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

    private static List<PrintingStockEntry> RemovalOrder(PrintingConsumable item) =>
        item.StockEntries
            .Where(entry => entry.Quantity > 0)
            .OrderBy(entry => entry.ExpirationDate ?? DateOnly.MaxValue)
            .ThenBy(entry => entry.PrintingStockEntryId)
            .ToList();

    private static int TotalQuantity(PrintingConsumable item) =>
        item.StockEntries.Where(entry => entry.Quantity > 0).Sum(entry => entry.Quantity);

    private static ConsumableAlert Alert(ConsumableRow row, string detail, DateOnly? expirationDate = null) => new()
    {
        PrintingConsumableId = row.PrintingConsumableId,
        Category = row.Category,
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
}
