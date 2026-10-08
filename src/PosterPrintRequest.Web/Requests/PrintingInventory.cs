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

public sealed class ConsumableUpdate
{
    public required int PrintingConsumableId { get; init; }

    public required int LowStockThreshold { get; init; }

    public required int CriticalStockThreshold { get; init; }

    public string? Name { get; init; }

    public string? Code { get; init; }

    public string? Capacity { get; init; }
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

    Task<InventoryOutcome> UpdateAsync(ConsumableUpdate update, CancellationToken cancellationToken);

    Task<InventoryOutcome> AddStockAsync(int printingConsumableId, int quantity, DateOnly? expirationDate, CancellationToken cancellationToken);

    Task<InventoryOutcome> RemoveStockAsync(
        int printingConsumableId,
        int quantity,
        bool earliestExpirationAcknowledged,
        DateOnly? acknowledgedExpirationDate,
        CancellationToken cancellationToken);

    Task<InventoryOutcome> AddLaminatingMaterialAsync(string? name, string? code, string? capacity, CancellationToken cancellationToken);
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
        await EnsureCatalogAsync(cancellationToken);
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

    public async Task<InventoryOutcome> UpdateAsync(ConsumableUpdate update, CancellationToken cancellationToken)
    {
        await EnsureCatalogAsync(cancellationToken);
        var item = await _db.PrintingConsumables
            .Include(candidate => candidate.StockEntries)
            .SingleOrDefaultAsync(candidate => candidate.PrintingConsumableId == update.PrintingConsumableId && candidate.Active, cancellationToken);
        if (item is null)
        {
            return InventoryOutcome.Failure("That consumable is not in the inventory.");
        }

        var thresholds = ValidateThresholds(update.LowStockThreshold, update.CriticalStockThreshold);
        if (thresholds is not null)
        {
            return thresholds;
        }

        if (item.Category == ConsumableCategory.Laminating)
        {
            var identity = await ApplyLaminatingIdentityAsync(item, update.Name, update.Code, update.Capacity, cancellationToken);
            if (identity is not null)
            {
                return identity;
            }
        }

        item.LowStockThreshold = update.LowStockThreshold;
        item.CriticalStockThreshold = update.CriticalStockThreshold;
        item.Status = ConsumableStock.FromQuantity(TotalQuantity(item), item.LowStockThreshold, item.CriticalStockThreshold);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Technician updated thresholds for {Consumable}. Status is {Status}.",
            item.Code ?? item.Name,
            item.Status);
        return InventoryOutcome.Success();
    }

    public async Task<InventoryOutcome> AddLaminatingMaterialAsync(string? name, string? code, string? capacity, CancellationToken cancellationToken)
    {
        var item = new PrintingConsumable
        {
            Category = ConsumableCategory.Laminating,
            HasExpirationDate = true,
            LowStockThreshold = 0,
            CriticalStockThreshold = 0,
            Status = ConsumableStock.Depleted,
            Active = true
        };
        var identity = await ApplyLaminatingIdentityAsync(item, name, code, capacity, cancellationToken);
        if (identity is not null)
        {
            return identity;
        }

        _db.PrintingConsumables.Add(item);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return InventoryOutcome.Failure("That code is already registered.");
        }

        _logger.LogInformation("Technician registered laminating material {Consumable}.", item.Code ?? item.Name);
        return InventoryOutcome.Success();
    }

    public async Task<InventoryOutcome> AddStockAsync(int printingConsumableId, int quantity, DateOnly? expirationDate, CancellationToken cancellationToken)
    {
        await EnsureCatalogAsync(cancellationToken);
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

        item.StockEntries.Add(new PrintingStockEntry
        {
            Quantity = quantity,
            ExpirationDate = expirationDate
        });
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
        await EnsureCatalogAsync(cancellationToken);
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
            .OrderBy(CatalogRank)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
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

    private static InventoryOutcome? ValidateThresholds(int low, int critical)
    {
        if (low < 0 || critical < 0)
        {
            return InventoryOutcome.Failure("Enter a threshold of zero or more.");
        }

        if (low < critical)
        {
            return InventoryOutcome.Failure("The low-stock threshold must be at least the critical threshold.");
        }

        return null;
    }

    private async Task<InventoryOutcome?> ApplyLaminatingIdentityAsync(
        PrintingConsumable item,
        string? name,
        string? code,
        string? capacity,
        CancellationToken cancellationToken)
    {
        var trimmedName = name?.Trim() ?? "";
        if (trimmedName.Length == 0)
        {
            return InventoryOutcome.Failure("Enter the material name.");
        }

        if (trimmedName.Length > PrintingConsumableConfiguration.NameMaxLength)
        {
            return InventoryOutcome.Failure("Enter a shorter material name.");
        }

        var trimmedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        if (trimmedCode is not null && trimmedCode.Length > PrintingConsumableConfiguration.CodeMaxLength)
        {
            return InventoryOutcome.Failure("Enter a shorter material code.");
        }

        var trimmedCapacity = string.IsNullOrWhiteSpace(capacity) ? null : capacity.Trim();
        if (trimmedCapacity is not null && trimmedCapacity.Length > PrintingConsumableConfiguration.CapacityMaxLength)
        {
            return InventoryOutcome.Failure("Enter a shorter capacity.");
        }

        if (trimmedCode is not null)
        {
            var taken = await _db.PrintingConsumables.AnyAsync(
                candidate => candidate.Code == trimmedCode && candidate.PrintingConsumableId != item.PrintingConsumableId,
                cancellationToken);
            if (taken)
            {
                return InventoryOutcome.Failure("That code is already registered.");
            }
        }

        item.Name = trimmedName;
        item.Code = trimmedCode;
        item.Capacity = trimmedCapacity;
        return null;
    }

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

    private async Task EnsureCatalogAsync(CancellationToken cancellationToken)
    {
        var codes = await _db.PrintingConsumables
            .Where(item => item.Code != null)
            .Select(item => item.Code!)
            .ToListAsync(cancellationToken);
        var known = new HashSet<string>(codes, StringComparer.Ordinal);
        var added = false;
        foreach (var entry in Catalog)
        {
            if (!known.Add(entry.Code))
            {
                continue;
            }

            _db.PrintingConsumables.Add(new PrintingConsumable
            {
                Category = entry.Category,
                Name = entry.Name,
                Code = entry.Code,
                Capacity = entry.Capacity,
                HasExpirationDate = entry.HasExpirationDate,
                LowStockThreshold = 0,
                CriticalStockThreshold = 0,
                Status = ConsumableStock.Depleted,
                Active = true
            });
            added = true;
        }

        if (added)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static int CatalogRank(PrintingConsumable item)
    {
        if (item.Code is null)
        {
            return Catalog.Length;
        }

        for (var index = 0; index < Catalog.Length; index++)
        {
            if (string.Equals(Catalog[index].Code, item.Code, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return Catalog.Length;
    }

    private static readonly CatalogEntry[] Catalog =
    [
        new(ConsumableCategory.Cartridge, "MK Matte Black", "C9403A", "130 ml", true),
        new(ConsumableCategory.Cartridge, "PK Photo Black", "C9370A", "130 ml", true),
        new(ConsumableCategory.Cartridge, "B Blue", "C9371A", "130 ml", true),
        new(ConsumableCategory.Cartridge, "M Magenta", "C9372A", "130 ml", true),
        new(ConsumableCategory.Cartridge, "Y Yellow", "C9373A", "130 ml", true),
        new(ConsumableCategory.Cartridge, "G Gray", "C9374A", "130 ml", true),
        new(ConsumableCategory.Paper, "HP Bright White Inkjet Paper", "C1861A", "36 × 150 ft, 90 g/m², 4.7 mil / 119 microns", false),
        new(ConsumableCategory.Paper, "HP Photo Paper / Photo Paper Gloss", "C6814A", "36 in, High Gloss", false)
    ];

    private sealed record CatalogEntry(string Category, string Name, string Code, string Capacity, bool HasExpirationDate);
}
