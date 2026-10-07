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

    public DateOnly? ExpirationDate { get; init; }
}

public sealed class PrintingInventoryView
{
    public int? ExpirationWarningDays { get; init; }

    public required IReadOnlyList<ConsumableRow> Cartridges { get; init; }

    public required IReadOnlyList<ConsumableRow> Paper { get; init; }

    public required IReadOnlyList<ConsumableRow> LaminatingMaterials { get; init; }
}

public sealed class ConsumableAlert
{
    public required int PrintingConsumableId { get; init; }

    public required string Name { get; init; }

    public string? Code { get; init; }

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

    public required int CurrentQuantity { get; init; }

    public required int LowStockThreshold { get; init; }

    public required int CriticalStockThreshold { get; init; }

    public DateOnly? ExpirationDate { get; init; }

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

    Task<ConsumableAttention> CriticalAlertsAsync(DateOnly today, CancellationToken cancellationToken);

    Task<InventoryOutcome> UpdateAsync(ConsumableUpdate update, CancellationToken cancellationToken);

    Task<InventoryOutcome> AddLaminatingMaterialAsync(string? name, string? code, string? capacity, CancellationToken cancellationToken);

    Task<InventoryOutcome> SetExpirationWarningDaysAsync(int? days, CancellationToken cancellationToken);
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
            .Where(item => item.Active)
            .ToListAsync(cancellationToken);
        var changed = ApplyDerivedStatus(items);
        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        var setting = await SettingAsync(cancellationToken);
        return new PrintingInventoryView
        {
            ExpirationWarningDays = setting.ExpirationWarningDays,
            Cartridges = Rows(items, ConsumableCategory.Cartridge),
            Paper = Rows(items, ConsumableCategory.Paper),
            LaminatingMaterials = Rows(items, ConsumableCategory.Laminating)
        };
    }

    public async Task<ConsumableAttention> CriticalAlertsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var view = await LoadAsync(cancellationToken);
        var rows = view.Cartridges.Concat(view.Paper).Concat(view.LaminatingMaterials).ToList();
        var depleted = new List<ConsumableAlert>();
        var lowStock = new List<ConsumableAlert>();
        var expiring = new List<ConsumableAlert>();
        foreach (var row in rows)
        {
            if (row.Status == ConsumableStock.Depleted)
            {
                depleted.Add(Alert(row, "Depleted, quantity " + row.CurrentQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            else if (row.Status == ConsumableStock.LowStock)
            {
                lowStock.Add(Alert(row, "Low stock, quantity " + row.CurrentQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            var expiration = ConsumableStock.ExpirationAlert(row.HasExpirationDate, row.ExpirationDate, today, view.ExpirationWarningDays);
            if (expiration is not null && row.ExpirationDate is not null)
            {
                expiring.Add(Alert(row, expiration + ", " + row.ExpirationDate.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        return new ConsumableAttention
        {
            Depleted = depleted,
            LowStock = lowStock,
            ExpiringSoon = expiring
        };
    }

    public async Task<InventoryOutcome> UpdateAsync(ConsumableUpdate update, CancellationToken cancellationToken)
    {
        await EnsureCatalogAsync(cancellationToken);
        var item = await _db.PrintingConsumables
            .SingleOrDefaultAsync(candidate => candidate.PrintingConsumableId == update.PrintingConsumableId && candidate.Active, cancellationToken);
        if (item is null)
        {
            return InventoryOutcome.Failure("That consumable is not in the inventory.");
        }

        var thresholds = ValidateThresholds(update.CurrentQuantity, update.LowStockThreshold, update.CriticalStockThreshold);
        if (thresholds is not null)
        {
            return thresholds;
        }

        if (!item.HasExpirationDate)
        {
            if (update.ExpirationDate is not null)
            {
                return InventoryOutcome.Failure("This material does not use an expiration date.");
            }
        }
        else if (update.CurrentQuantity > 0 && update.ExpirationDate is null)
        {
            return InventoryOutcome.Failure("Enter the expiration date.");
        }

        if (item.Category == ConsumableCategory.Laminating)
        {
            var identity = await ApplyLaminatingIdentityAsync(item, update.Name, update.Code, update.Capacity, cancellationToken);
            if (identity is not null)
            {
                return identity;
            }
        }

        item.CurrentQuantity = update.CurrentQuantity;
        item.LowStockThreshold = update.LowStockThreshold;
        item.CriticalStockThreshold = update.CriticalStockThreshold;
        item.ExpirationDate = item.HasExpirationDate ? update.ExpirationDate : null;
        item.Status = ConsumableStock.FromQuantity(item.CurrentQuantity, item.LowStockThreshold, item.CriticalStockThreshold);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Technician updated consumable {Consumable} to quantity {Quantity} and status {Status}.",
            item.Code ?? item.Name,
            item.CurrentQuantity,
            item.Status);
        return InventoryOutcome.Success();
    }

    public async Task<InventoryOutcome> AddLaminatingMaterialAsync(string? name, string? code, string? capacity, CancellationToken cancellationToken)
    {
        var item = new PrintingConsumable
        {
            Category = ConsumableCategory.Laminating,
            HasExpirationDate = true,
            CurrentQuantity = 0,
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

    public async Task<InventoryOutcome> SetExpirationWarningDaysAsync(int? days, CancellationToken cancellationToken)
    {
        if (days is < 0)
        {
            return InventoryOutcome.Failure("Enter a warning period of zero or more days.");
        }

        if (days is not null && days.Value > DateOnly.MaxValue.DayNumber)
        {
            return InventoryOutcome.Failure("Enter a shorter warning period.");
        }

        var setting = await SettingAsync(cancellationToken);
        setting.ExpirationWarningDays = days;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Technician set the consumable expiration warning to {Days} days.", days);
        return InventoryOutcome.Success();
    }

    private static IReadOnlyList<ConsumableRow> Rows(IEnumerable<PrintingConsumable> items, string category)
    {
        return items
            .Where(item => item.Category == category)
            .OrderBy(CatalogRank)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .Select(ToRow)
            .ToList();
    }

    private static ConsumableRow ToRow(PrintingConsumable item) => new()
    {
        PrintingConsumableId = item.PrintingConsumableId,
        Category = item.Category,
        Name = item.Name,
        Code = item.Code,
        Capacity = item.Capacity,
        HasExpirationDate = item.HasExpirationDate,
        CurrentQuantity = item.CurrentQuantity,
        LowStockThreshold = item.LowStockThreshold,
        CriticalStockThreshold = item.CriticalStockThreshold,
        Status = item.Status,
        ExpirationDate = item.ExpirationDate
    };

    private static ConsumableAlert Alert(ConsumableRow row, string detail) => new()
    {
        PrintingConsumableId = row.PrintingConsumableId,
        Name = row.Name,
        Code = row.Code,
        Detail = detail
    };

    private static InventoryOutcome? ValidateThresholds(int quantity, int low, int critical)
    {
        if (quantity < 0)
        {
            return InventoryOutcome.Failure("Enter a quantity of zero or more.");
        }

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
            var status = ConsumableStock.FromQuantity(item.CurrentQuantity, item.LowStockThreshold, item.CriticalStockThreshold);
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
                CurrentQuantity = 0,
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

    private async Task<PrintingInventorySetting> SettingAsync(CancellationToken cancellationToken)
    {
        var setting = await _db.PrintingInventorySettings.SingleOrDefaultAsync(cancellationToken);
        if (setting is not null)
        {
            return setting;
        }

        setting = new PrintingInventorySetting();
        _db.PrintingInventorySettings.Add(setting);
        await _db.SaveChangesAsync(cancellationToken);
        return setting;
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
