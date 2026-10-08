using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web.Requests;

public sealed class ConfigurationOutcome
{
    public bool Completed { get; init; }

    public string? Message { get; init; }

    public static ConfigurationOutcome Success() => new() { Completed = true };

    public static ConfigurationOutcome Failure(string message) => new() { Completed = false, Message = message };
}

public sealed class ConfiguredValue
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required bool Active { get; init; }
}

public sealed class ConfiguredReason
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required bool Active { get; init; }

    public required bool RequiresMentor { get; init; }

    public required bool RequiresApprovalSheet { get; init; }
}

public sealed class ConfiguredPrinterModel
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required bool Active { get; init; }

    public required IReadOnlyList<int> CompatibleConsumableIds { get; init; }
}

public sealed class ConfiguredConsumable
{
    public required int Id { get; init; }

    public required string Category { get; init; }

    public required string Name { get; init; }

    public string? Code { get; init; }

    public string? Capacity { get; init; }

    public required bool HasExpirationDate { get; init; }

    public required int LowStockThreshold { get; init; }

    public required int CriticalStockThreshold { get; init; }

    public required bool Active { get; init; }
}

public sealed class OperatorConfigurationView
{
    public required IReadOnlyList<ConfiguredValue> Departments { get; init; }

    public required IReadOnlyList<ConfiguredReason> Reasons { get; init; }

    public required IReadOnlyList<ConfiguredPrinterModel> PrinterModels { get; init; }

    public required IReadOnlyList<ConfiguredConsumable> Consumables { get; init; }
}

public interface IOperatorConfiguration
{
    Task<OperatorConfigurationView> LoadAsync(CancellationToken cancellationToken);

    Task<ConfigurationOutcome> AddDepartmentAsync(string? name, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> UpdateDepartmentAsync(int departmentId, string? name, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> SetDepartmentActiveAsync(int departmentId, bool active, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> DeleteDepartmentAsync(int departmentId, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> AddReasonAsync(string? name, bool requiresMentor, bool requiresApprovalSheet, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> UpdateReasonAsync(int reasonId, string? name, bool requiresMentor, bool requiresApprovalSheet, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> SetReasonActiveAsync(int reasonId, bool active, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> DeleteReasonAsync(int reasonId, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> AddPrinterModelAsync(string? name, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> UpdatePrinterModelAsync(int printerModelId, string? name, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> SetPrinterModelActiveAsync(int printerModelId, bool active, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> DeletePrinterModelAsync(int printerModelId, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> AddConsumableAsync(string? category, string? name, string? code, string? capacity, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> UpdateConsumableAsync(int printingConsumableId, string? category, string? name, string? code, string? capacity, int lowStockThreshold, int criticalStockThreshold, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> SetConsumableActiveAsync(int printingConsumableId, bool active, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> DeleteConsumableAsync(int printingConsumableId, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> SetConsumableThresholdsAsync(int printingConsumableId, int lowStockThreshold, int criticalStockThreshold, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> SetCompatibilityAsync(int printerModelId, int printingConsumableId, bool compatible, CancellationToken cancellationToken);
}

public sealed class OperatorConfiguration : IOperatorConfiguration
{
    private readonly PosterPrintRequestDbContext _db;
    private readonly ILogger<OperatorConfiguration> _logger;

    public OperatorConfiguration(PosterPrintRequestDbContext db, ILogger<OperatorConfiguration> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<OperatorConfigurationView> LoadAsync(CancellationToken cancellationToken)
    {
        var departments = await _db.Departments
            .OrderBy(department => department.Name)
            .Select(department => new ConfiguredValue
            {
                Id = department.DepartmentId,
                Name = department.Name,
                Active = department.Active
            })
            .ToListAsync(cancellationToken);

        var reasons = await _db.Reasons
            .OrderBy(reason => reason.Name)
            .Select(reason => new ConfiguredReason
            {
                Id = reason.ReasonId,
                Name = reason.Name,
                Active = reason.Active,
                RequiresMentor = reason.RequiresMentor,
                RequiresApprovalSheet = reason.RequiresApprovalSheet
            })
            .ToListAsync(cancellationToken);

        var printers = await _db.PrinterModels
            .OrderBy(model => model.Name)
            .ToListAsync(cancellationToken);
        var links = await _db.PrinterModelConsumables
            .Where(link => link.Active)
            .Select(link => new { link.PrinterModelId, link.PrintingConsumableId })
            .ToListAsync(cancellationToken);
        var compatibleByPrinter = links
            .GroupBy(link => link.PrinterModelId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<int>)group.Select(link => link.PrintingConsumableId).ToArray());

        var consumables = await _db.PrintingConsumables
            .Select(item => new ConfiguredConsumable
            {
                Id = item.PrintingConsumableId,
                Category = item.Category,
                Name = item.Name,
                Code = item.Code,
                Capacity = item.Capacity,
                HasExpirationDate = item.HasExpirationDate,
                LowStockThreshold = item.LowStockThreshold,
                CriticalStockThreshold = item.CriticalStockThreshold,
                Active = item.Active
            })
            .ToListAsync(cancellationToken);

        return new OperatorConfigurationView
        {
            Departments = departments,
            Reasons = reasons,
            PrinterModels = printers
                .Select(model => new ConfiguredPrinterModel
                {
                    Id = model.PrinterModelId,
                    Name = model.Name,
                    Active = model.Active,
                    CompatibleConsumableIds = compatibleByPrinter.TryGetValue(model.PrinterModelId, out var ids) ? ids : []
                })
                .ToList(),
            Consumables = consumables
                .OrderBy(CategoryRank)
                .ThenBy(item => item.Name, StringComparer.Ordinal)
                .ToList()
        };
    }

    public async Task<ConfigurationOutcome> AddDepartmentAsync(string? name, CancellationToken cancellationToken)
    {
        if (!TryReadName(name, DepartmentConfiguration.NameMaxLength, "Enter a department.", "Enter a shorter department.", out var trimmed, out var invalid))
        {
            return invalid!;
        }

        if (await NameTakenAsync(_db.Departments.Select(department => department.Name), trimmed, cancellationToken))
        {
            return ConfigurationOutcome.Failure("That department is already in the list.");
        }

        _db.Departments.Add(new Department { Name = trimmed, Active = true });
        var saved = await SaveAsync("That department is already in the list.", cancellationToken);
        if (saved.Completed)
        {
            _logger.LogInformation("Operator added department {Department}.", trimmed);
        }

        return saved;
    }

    public async Task<ConfigurationOutcome> UpdateDepartmentAsync(int departmentId, string? name, CancellationToken cancellationToken)
    {
        if (!TryReadName(name, DepartmentConfiguration.NameMaxLength, "Enter a department.", "Enter a shorter department.", out var trimmed, out var invalid))
        {
            return invalid!;
        }

        var department = await _db.Departments.SingleOrDefaultAsync(candidate => candidate.DepartmentId == departmentId, cancellationToken);
        if (department is null)
        {
            return ConfigurationOutcome.Failure("That department is not in the list.");
        }

        if (await NameTakenAsync(_db.Departments.Where(candidate => candidate.DepartmentId != departmentId).Select(candidate => candidate.Name), trimmed, cancellationToken))
        {
            return ConfigurationOutcome.Failure("That department is already in the list.");
        }

        department.Name = trimmed;
        var saved = await SaveAsync("That department is already in the list.", cancellationToken);
        if (saved.Completed)
        {
            _logger.LogInformation("Operator renamed department {DepartmentId} to {Department}.", departmentId, trimmed);
        }

        return saved;
    }

    public async Task<ConfigurationOutcome> SetDepartmentActiveAsync(int departmentId, bool active, CancellationToken cancellationToken)
    {
        var department = await _db.Departments.SingleOrDefaultAsync(candidate => candidate.DepartmentId == departmentId, cancellationToken);
        if (department is null)
        {
            return ConfigurationOutcome.Failure("That department is not in the list.");
        }

        department.Active = active;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Operator set department {Department} active to {Active}.", department.Name, active);
        return ConfigurationOutcome.Success();
    }

    public async Task<ConfigurationOutcome> DeleteDepartmentAsync(int departmentId, CancellationToken cancellationToken)
    {
        var department = await _db.Departments.SingleOrDefaultAsync(candidate => candidate.DepartmentId == departmentId, cancellationToken);
        if (department is null)
        {
            return ConfigurationOutcome.Failure("That department is not in the list.");
        }

        if (await _db.PosterRequests.AnyAsync(request => request.DepartmentId == departmentId, cancellationToken))
        {
            return ConfigurationOutcome.Failure("This department cannot be deleted because it is referenced by an existing request.");
        }

        var name = department.Name;
        var deleted = await DeleteAsync(department, "This department cannot be deleted because it is referenced by an existing request.", cancellationToken);
        if (deleted.Completed)
        {
            _logger.LogInformation("Operator deleted department {Department}.", name);
        }

        return deleted;
    }

    public async Task<ConfigurationOutcome> AddReasonAsync(string? name, bool requiresMentor, bool requiresApprovalSheet, CancellationToken cancellationToken)
    {
        if (!TryReadName(name, ReasonConfiguration.NameMaxLength, "Enter an event.", "Enter a shorter event.", out var trimmed, out var invalid))
        {
            return invalid!;
        }

        if (await NameTakenAsync(_db.Reasons.Select(reason => reason.Name), trimmed, cancellationToken))
        {
            return ConfigurationOutcome.Failure("That event is already in the list.");
        }

        _db.Reasons.Add(new Reason
        {
            Name = trimmed,
            Active = true,
            RequiresMentor = requiresMentor,
            RequiresApprovalSheet = requiresApprovalSheet
        });
        var saved = await SaveAsync("That event is already in the list.", cancellationToken);
        if (saved.Completed)
        {
            _logger.LogInformation("Operator added event {Event}.", trimmed);
        }

        return saved;
    }

    public async Task<ConfigurationOutcome> UpdateReasonAsync(int reasonId, string? name, bool requiresMentor, bool requiresApprovalSheet, CancellationToken cancellationToken)
    {
        if (!TryReadName(name, ReasonConfiguration.NameMaxLength, "Enter an event.", "Enter a shorter event.", out var trimmed, out var invalid))
        {
            return invalid!;
        }

        var reason = await _db.Reasons.SingleOrDefaultAsync(candidate => candidate.ReasonId == reasonId, cancellationToken);
        if (reason is null)
        {
            return ConfigurationOutcome.Failure("That event is not in the list.");
        }

        if (await NameTakenAsync(_db.Reasons.Where(candidate => candidate.ReasonId != reasonId).Select(candidate => candidate.Name), trimmed, cancellationToken))
        {
            return ConfigurationOutcome.Failure("That event is already in the list.");
        }

        reason.Name = trimmed;
        reason.RequiresMentor = requiresMentor;
        reason.RequiresApprovalSheet = requiresApprovalSheet;
        var saved = await SaveAsync("That event is already in the list.", cancellationToken);
        if (saved.Completed)
        {
            _logger.LogInformation("Operator renamed event {ReasonId} to {Event}.", reasonId, trimmed);
        }

        return saved;
    }

    public async Task<ConfigurationOutcome> SetReasonActiveAsync(int reasonId, bool active, CancellationToken cancellationToken)
    {
        var reason = await _db.Reasons.SingleOrDefaultAsync(candidate => candidate.ReasonId == reasonId, cancellationToken);
        if (reason is null)
        {
            return ConfigurationOutcome.Failure("That event is not in the list.");
        }

        reason.Active = active;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Operator set event {Event} active to {Active}.", reason.Name, active);
        return ConfigurationOutcome.Success();
    }

    public async Task<ConfigurationOutcome> DeleteReasonAsync(int reasonId, CancellationToken cancellationToken)
    {
        var reason = await _db.Reasons.SingleOrDefaultAsync(candidate => candidate.ReasonId == reasonId, cancellationToken);
        if (reason is null)
        {
            return ConfigurationOutcome.Failure("That event is not in the list.");
        }

        if (await _db.PosterRequests.AnyAsync(request => request.ReasonId == reasonId, cancellationToken))
        {
            return ConfigurationOutcome.Failure("This event cannot be deleted because it is referenced by an existing request.");
        }

        var name = reason.Name;
        var deleted = await DeleteAsync(reason, "This event cannot be deleted because it is referenced by an existing request.", cancellationToken);
        if (deleted.Completed)
        {
            _logger.LogInformation("Operator deleted event {Event}.", name);
        }

        return deleted;
    }

    public async Task<ConfigurationOutcome> AddPrinterModelAsync(string? name, CancellationToken cancellationToken)
    {
        if (!TryReadName(name, PrinterModelConfiguration.NameMaxLength, "Enter a printer model.", "Enter a shorter printer model.", out var trimmed, out var invalid))
        {
            return invalid!;
        }

        if (await NameTakenAsync(_db.PrinterModels.Select(model => model.Name), trimmed, cancellationToken))
        {
            return ConfigurationOutcome.Failure("That printer model is already in the list.");
        }

        _db.PrinterModels.Add(new PrinterModel { Name = trimmed, Active = true });
        var saved = await SaveAsync("That printer model is already in the list.", cancellationToken);
        if (saved.Completed)
        {
            _logger.LogInformation("Operator added printer model {PrinterModel}.", trimmed);
        }

        return saved;
    }

    public async Task<ConfigurationOutcome> UpdatePrinterModelAsync(int printerModelId, string? name, CancellationToken cancellationToken)
    {
        if (!TryReadName(name, PrinterModelConfiguration.NameMaxLength, "Enter a printer model.", "Enter a shorter printer model.", out var trimmed, out var invalid))
        {
            return invalid!;
        }

        var model = await _db.PrinterModels.SingleOrDefaultAsync(candidate => candidate.PrinterModelId == printerModelId, cancellationToken);
        if (model is null)
        {
            return ConfigurationOutcome.Failure("That printer model is not in the list.");
        }

        if (await NameTakenAsync(_db.PrinterModels.Where(candidate => candidate.PrinterModelId != printerModelId).Select(candidate => candidate.Name), trimmed, cancellationToken))
        {
            return ConfigurationOutcome.Failure("That printer model is already in the list.");
        }

        model.Name = trimmed;
        var saved = await SaveAsync("That printer model is already in the list.", cancellationToken);
        if (saved.Completed)
        {
            _logger.LogInformation("Operator renamed printer model {PrinterModelId} to {PrinterModel}.", printerModelId, trimmed);
        }

        return saved;
    }

    public async Task<ConfigurationOutcome> SetPrinterModelActiveAsync(int printerModelId, bool active, CancellationToken cancellationToken)
    {
        var model = await _db.PrinterModels.SingleOrDefaultAsync(candidate => candidate.PrinterModelId == printerModelId, cancellationToken);
        if (model is null)
        {
            return ConfigurationOutcome.Failure("That printer model is not in the list.");
        }

        model.Active = active;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Operator set printer model {PrinterModel} active to {Active}.", model.Name, active);
        return ConfigurationOutcome.Success();
    }

    public async Task<ConfigurationOutcome> DeletePrinterModelAsync(int printerModelId, CancellationToken cancellationToken)
    {
        var model = await _db.PrinterModels.SingleOrDefaultAsync(candidate => candidate.PrinterModelId == printerModelId, cancellationToken);
        if (model is null)
        {
            return ConfigurationOutcome.Failure("That printer model is not in the list.");
        }

        if (await _db.PrinterModelConsumables.AnyAsync(link => link.PrinterModelId == printerModelId, cancellationToken))
        {
            return ConfigurationOutcome.Failure("This printer model cannot be deleted because a material is associated with it.");
        }

        var name = model.Name;
        var deleted = await DeleteAsync(model, "This printer model cannot be deleted because a material is associated with it.", cancellationToken);
        if (deleted.Completed)
        {
            _logger.LogInformation("Operator deleted printer model {PrinterModel}.", name);
        }

        return deleted;
    }

    public async Task<ConfigurationOutcome> AddConsumableAsync(string? category, string? name, string? code, string? capacity, CancellationToken cancellationToken)
    {
        var selectedCategory = category?.Trim() ?? "";
        if (selectedCategory is not (ConsumableCategory.Cartridge or ConsumableCategory.Paper or ConsumableCategory.Laminating))
        {
            return ConfigurationOutcome.Failure("Choose a category.");
        }

        if (!TryReadName(name, PrintingConsumableConfiguration.NameMaxLength, "Enter a material name.", "Enter a shorter material name.", out var trimmedName, out var invalidName))
        {
            return invalidName!;
        }

        var trimmedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        if (trimmedCode is not null && trimmedCode.Length > PrintingConsumableConfiguration.CodeMaxLength)
        {
            return ConfigurationOutcome.Failure("Enter a shorter material code.");
        }

        var trimmedCapacity = string.IsNullOrWhiteSpace(capacity) ? null : capacity.Trim();
        if (trimmedCapacity is not null && trimmedCapacity.Length > PrintingConsumableConfiguration.CapacityMaxLength)
        {
            return ConfigurationOutcome.Failure("Enter a shorter capacity.");
        }

        var existing = await _db.PrintingConsumables
            .Select(item => new { item.Category, item.Name, item.Code })
            .ToListAsync(cancellationToken);
        if (existing.Any(item => item.Category == selectedCategory && string.Equals(item.Name.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return ConfigurationOutcome.Failure("That material is already in this category.");
        }

        if (trimmedCode is not null && existing.Any(item => string.Equals(item.Code, trimmedCode, StringComparison.Ordinal)))
        {
            return ConfigurationOutcome.Failure("That code is already registered.");
        }

        _db.PrintingConsumables.Add(new PrintingConsumable
        {
            Category = selectedCategory,
            Name = trimmedName,
            Code = trimmedCode,
            Capacity = trimmedCapacity,
            HasExpirationDate = selectedCategory != ConsumableCategory.Paper,
            LowStockThreshold = 0,
            CriticalStockThreshold = 0,
            Status = ConsumableStock.Depleted,
            Active = true
        });
        var saved = await SaveAsync("That code is already registered.", cancellationToken);
        if (saved.Completed)
        {
            _logger.LogInformation("Operator added {Category} {Consumable}.", selectedCategory, trimmedCode ?? trimmedName);
        }

        return saved;
    }

    public async Task<ConfigurationOutcome> UpdateConsumableAsync(
        int printingConsumableId,
        string? category,
        string? name,
        string? code,
        string? capacity,
        int lowStockThreshold,
        int criticalStockThreshold,
        CancellationToken cancellationToken)
    {
        var selectedCategory = category?.Trim() ?? "";
        if (selectedCategory is not (ConsumableCategory.Cartridge or ConsumableCategory.Paper or ConsumableCategory.Laminating))
        {
            return ConfigurationOutcome.Failure("Choose a category.");
        }

        if (!TryReadName(name, PrintingConsumableConfiguration.NameMaxLength, "Enter a material name.", "Enter a shorter material name.", out var trimmedName, out var invalidName))
        {
            return invalidName!;
        }

        var trimmedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        if (trimmedCode is not null && trimmedCode.Length > PrintingConsumableConfiguration.CodeMaxLength)
        {
            return ConfigurationOutcome.Failure("Enter a shorter material code.");
        }

        var trimmedCapacity = string.IsNullOrWhiteSpace(capacity) ? null : capacity.Trim();
        if (trimmedCapacity is not null && trimmedCapacity.Length > PrintingConsumableConfiguration.CapacityMaxLength)
        {
            return ConfigurationOutcome.Failure("Enter a shorter capacity.");
        }

        if (lowStockThreshold < 0 || criticalStockThreshold < 0)
        {
            return ConfigurationOutcome.Failure("Enter a threshold of zero or more.");
        }

        if (lowStockThreshold < criticalStockThreshold)
        {
            return ConfigurationOutcome.Failure("The low-stock threshold must be at least the critical threshold.");
        }

        var item = await _db.PrintingConsumables
            .Include(candidate => candidate.StockEntries)
            .SingleOrDefaultAsync(candidate => candidate.PrintingConsumableId == printingConsumableId, cancellationToken);
        if (item is null)
        {
            return ConfigurationOutcome.Failure("That consumable is not in the list.");
        }

        if (item.Category != selectedCategory && item.StockEntries.Count > 0)
        {
            return ConfigurationOutcome.Failure("This material has stock, so its category stays the same.");
        }

        var existing = await _db.PrintingConsumables
            .Where(candidate => candidate.PrintingConsumableId != printingConsumableId)
            .Select(candidate => new { candidate.Category, candidate.Name, candidate.Code })
            .ToListAsync(cancellationToken);
        if (existing.Any(candidate => candidate.Category == selectedCategory && string.Equals(candidate.Name.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return ConfigurationOutcome.Failure("That material is already in this category.");
        }

        if (trimmedCode is not null && existing.Any(candidate => string.Equals(candidate.Code, trimmedCode, StringComparison.Ordinal)))
        {
            return ConfigurationOutcome.Failure("That code is already registered.");
        }

        item.Category = selectedCategory;
        item.Name = trimmedName;
        item.Code = trimmedCode;
        item.Capacity = trimmedCapacity;
        item.HasExpirationDate = selectedCategory != ConsumableCategory.Paper;
        item.LowStockThreshold = lowStockThreshold;
        item.CriticalStockThreshold = criticalStockThreshold;
        var quantity = item.StockEntries.Where(entry => entry.Quantity > 0).Sum(entry => entry.Quantity);
        item.Status = ConsumableStock.FromQuantity(quantity, lowStockThreshold, criticalStockThreshold);
        var saved = await SaveAsync("That code is already registered.", cancellationToken);
        if (saved.Completed)
        {
            _logger.LogInformation("Operator updated {Category} {Consumable}.", selectedCategory, trimmedCode ?? trimmedName);
        }

        return saved;
    }

    public async Task<ConfigurationOutcome> SetConsumableActiveAsync(int printingConsumableId, bool active, CancellationToken cancellationToken)
    {
        var item = await _db.PrintingConsumables.SingleOrDefaultAsync(candidate => candidate.PrintingConsumableId == printingConsumableId, cancellationToken);
        if (item is null)
        {
            return ConfigurationOutcome.Failure("That consumable is not in the list.");
        }

        item.Active = active;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Operator set consumable {Consumable} active to {Active}.", item.Code ?? item.Name, active);
        return ConfigurationOutcome.Success();
    }

    public async Task<ConfigurationOutcome> DeleteConsumableAsync(int printingConsumableId, CancellationToken cancellationToken)
    {
        var item = await _db.PrintingConsumables.SingleOrDefaultAsync(candidate => candidate.PrintingConsumableId == printingConsumableId, cancellationToken);
        if (item is null)
        {
            return ConfigurationOutcome.Failure("That consumable is not in the list.");
        }

        if (await _db.PrintingStockEntries.AnyAsync(entry => entry.PrintingConsumableId == printingConsumableId, cancellationToken))
        {
            return ConfigurationOutcome.Failure("This material cannot be deleted because inventory stock references it.");
        }

        if (await _db.PrinterModelConsumables.AnyAsync(link => link.PrintingConsumableId == printingConsumableId, cancellationToken))
        {
            return ConfigurationOutcome.Failure("This material cannot be deleted because a printer model is associated with it.");
        }

        var name = item.Code ?? item.Name;
        var deleted = await DeleteAsync(item, "This material cannot be deleted because inventory stock references it.", cancellationToken);
        if (deleted.Completed)
        {
            _logger.LogInformation("Operator deleted consumable {Consumable}.", name);
        }

        return deleted;
    }

    public async Task<ConfigurationOutcome> SetConsumableThresholdsAsync(
        int printingConsumableId,
        int lowStockThreshold,
        int criticalStockThreshold,
        CancellationToken cancellationToken)
    {
        if (lowStockThreshold < 0 || criticalStockThreshold < 0)
        {
            return ConfigurationOutcome.Failure("Enter a threshold of zero or more.");
        }

        if (lowStockThreshold < criticalStockThreshold)
        {
            return ConfigurationOutcome.Failure("The low-stock threshold must be at least the critical threshold.");
        }

        var item = await _db.PrintingConsumables
            .Include(candidate => candidate.StockEntries)
            .SingleOrDefaultAsync(candidate => candidate.PrintingConsumableId == printingConsumableId, cancellationToken);
        if (item is null)
        {
            return ConfigurationOutcome.Failure("That consumable is not in the list.");
        }

        item.LowStockThreshold = lowStockThreshold;
        item.CriticalStockThreshold = criticalStockThreshold;
        var quantity = item.StockEntries.Where(entry => entry.Quantity > 0).Sum(entry => entry.Quantity);
        item.Status = ConsumableStock.FromQuantity(quantity, lowStockThreshold, criticalStockThreshold);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Operator set thresholds for {Consumable}. Low stock is {LowStock} and critical is {Critical}.",
            item.Code ?? item.Name,
            lowStockThreshold,
            criticalStockThreshold);
        return ConfigurationOutcome.Success();
    }

    public async Task<ConfigurationOutcome> SetCompatibilityAsync(int printerModelId, int printingConsumableId, bool compatible, CancellationToken cancellationToken)
    {
        var printerExists = await _db.PrinterModels.AnyAsync(model => model.PrinterModelId == printerModelId, cancellationToken);
        if (!printerExists)
        {
            return ConfigurationOutcome.Failure("That printer model is not in the list.");
        }

        var consumableExists = await _db.PrintingConsumables.AnyAsync(item => item.PrintingConsumableId == printingConsumableId, cancellationToken);
        if (!consumableExists)
        {
            return ConfigurationOutcome.Failure("That consumable is not in the list.");
        }

        var link = await _db.PrinterModelConsumables.SingleOrDefaultAsync(
            candidate => candidate.PrinterModelId == printerModelId && candidate.PrintingConsumableId == printingConsumableId,
            cancellationToken);
        if (link is null)
        {
            if (!compatible)
            {
                return ConfigurationOutcome.Success();
            }

            _db.PrinterModelConsumables.Add(new PrinterModelConsumable
            {
                PrinterModelId = printerModelId,
                PrintingConsumableId = printingConsumableId,
                Active = true
            });
        }
        else
        {
            link.Active = compatible;
        }

        var saved = await SaveAsync("That material is already associated with this printer model.", cancellationToken);
        if (saved.Completed)
        {
            _logger.LogInformation(
                "Operator set compatibility between printer {PrinterModelId} and consumable {PrintingConsumableId} to {Compatible}.",
                printerModelId,
                printingConsumableId,
                compatible);
        }

        return saved;
    }

    private async Task<bool> NameTakenAsync(IQueryable<string> names, string name, CancellationToken cancellationToken)
    {
        var existing = await names.ToListAsync(cancellationToken);
        return existing.Any(candidate => string.Equals(candidate.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ConfigurationOutcome> SaveAsync(string duplicateMessage, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return ConfigurationOutcome.Success();
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            foreach (var entry in _db.ChangeTracker.Entries().ToList())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.State = EntityState.Detached;
                }
                else if (entry.State == EntityState.Modified)
                {
                    await entry.ReloadAsync(cancellationToken);
                }
            }

            return ConfigurationOutcome.Failure(duplicateMessage);
        }
    }

    private async Task<ConfigurationOutcome> DeleteAsync<TEntity>(TEntity entity, string referencedMessage, CancellationToken cancellationToken)
        where TEntity : class
    {
        _db.Remove(entity);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return ConfigurationOutcome.Success();
        }
        catch (DbUpdateException exception) when (IsReferenceViolation(exception))
        {
            foreach (var entry in _db.ChangeTracker.Entries().Where(candidate => candidate.State == EntityState.Deleted).ToList())
            {
                entry.State = EntityState.Unchanged;
            }

            return ConfigurationOutcome.Failure(referencedMessage);
        }
    }

    private static bool TryReadName(string? value, int maxLength, string emptyMessage, string lengthMessage, out string name, out ConfigurationOutcome? failure)
    {
        name = value?.Trim() ?? "";
        if (name.Length == 0)
        {
            failure = ConfigurationOutcome.Failure(emptyMessage);
            return false;
        }

        if (name.Length > maxLength)
        {
            failure = ConfigurationOutcome.Failure(lengthMessage);
            return false;
        }

        failure = null;
        return true;
    }

    private static int CategoryRank(ConfiguredConsumable item) => item.Category switch
    {
        ConsumableCategory.Cartridge => 0,
        ConsumableCategory.Paper => 1,
        ConsumableCategory.Laminating => 2,
        _ => 3
    };

    private static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number is 2601 or 2627)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReferenceViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number == 547)
            {
                return true;
            }
        }

        return false;
    }
}
