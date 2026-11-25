using System.Data;
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

/// <summary>A consumable type as Configuration shows it (Consumables Inventory Contract section 4).</summary>
public sealed class ConfiguredConsumableType
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required string StockMode { get; init; }

    public required bool AcknowledgeEarliestExpiration { get; init; }

    public required bool UsesPrinterCompatibility { get; init; }

    public required int DisplayOrder { get; init; }

    public required bool Active { get; init; }

    public required int ConsumableCount { get; init; }
}

public sealed class ConfiguredConsumable
{
    public required int Id { get; init; }

    public required int ConsumableTypeId { get; init; }

    public required string TypeName { get; init; }

    public required bool TypeActive { get; init; }

    public required bool UsesPrinterCompatibility { get; init; }

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

    public required IReadOnlyList<ConfiguredConsumableType> ConsumableTypes { get; init; }

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

    Task<ConfigurationOutcome> AddConsumableTypeAsync(string? name, string? stockMode, bool acknowledgeEarliestExpiration, bool usesPrinterCompatibility, int displayOrder, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> UpdateConsumableTypeAsync(int consumableTypeId, string? name, string? stockMode, bool acknowledgeEarliestExpiration, bool usesPrinterCompatibility, int displayOrder, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> SetConsumableTypeActiveAsync(int consumableTypeId, bool active, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> DeleteConsumableTypeAsync(int consumableTypeId, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> AddConsumableAsync(int? consumableTypeId, string? name, string? code, string? capacity, CancellationToken cancellationToken);

    Task<ConfigurationOutcome> UpdateConsumableAsync(int printingConsumableId, int? consumableTypeId, string? name, string? code, string? capacity, int lowStockThreshold, int criticalStockThreshold, CancellationToken cancellationToken);

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

        var types = await _db.ConsumableTypes
            .OrderBy(type => type.DisplayOrder)
            .ThenBy(type => type.Name)
            .Select(type => new ConfiguredConsumableType
            {
                Id = type.ConsumableTypeId,
                Name = type.Name,
                StockMode = type.StockMode,
                AcknowledgeEarliestExpiration = type.AcknowledgeEarliestExpiration,
                UsesPrinterCompatibility = type.UsesPrinterCompatibility,
                DisplayOrder = type.DisplayOrder,
                Active = type.Active,
                ConsumableCount = type.Consumables.Count
            })
            .ToListAsync(cancellationToken);
        var typeRank = types.Select((type, index) => (type.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);

        var consumables = await _db.PrintingConsumables
            .Select(item => new ConfiguredConsumable
            {
                Id = item.PrintingConsumableId,
                ConsumableTypeId = item.ConsumableTypeId,
                TypeName = item.ConsumableType.Name,
                TypeActive = item.ConsumableType.Active,
                UsesPrinterCompatibility = item.ConsumableType.UsesPrinterCompatibility,
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
            ConsumableTypes = types,
            Consumables = consumables
                .OrderBy(item => typeRank.TryGetValue(item.ConsumableTypeId, out var rank) ? rank : int.MaxValue)
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

    // ---- Consumable types (Consumables Inventory Contract section 4) ----

    public const string TypeNameTaken = "That type name is already in use.";

    public async Task<ConfigurationOutcome> AddConsumableTypeAsync(
        string? name,
        string? stockMode,
        bool acknowledgeEarliestExpiration,
        bool usesPrinterCompatibility,
        int displayOrder,
        CancellationToken cancellationToken)
    {
        var invalid = CheckType(name, stockMode, acknowledgeEarliestExpiration, displayOrder, out var trimmed);
        if (invalid is not null)
        {
            return invalid;
        }

        if (await TypeNameTakenAsync(trimmed, null, cancellationToken))
        {
            return ConfigurationOutcome.Failure(TypeNameTaken);
        }

        _db.ConsumableTypes.Add(new ConsumableType
        {
            Name = trimmed,
            StockMode = stockMode!,
            AcknowledgeEarliestExpiration = acknowledgeEarliestExpiration,
            UsesPrinterCompatibility = usesPrinterCompatibility,
            DisplayOrder = displayOrder,
            Active = true
        });
        var saved = await SaveAsync(TypeNameTaken, cancellationToken);
        if (saved.Completed)
        {
            _logger.LogInformation("Operator added consumable type {ConsumableType}.", trimmed);
        }

        return saved;
    }

    public Task<ConfigurationOutcome> UpdateConsumableTypeAsync(
        int consumableTypeId,
        string? name,
        string? stockMode,
        bool acknowledgeEarliestExpiration,
        bool usesPrinterCompatibility,
        int displayOrder,
        CancellationToken cancellationToken) =>
        SerializableAsync(async token =>
        {
            var invalid = CheckType(name, stockMode, acknowledgeEarliestExpiration, displayOrder, out var trimmed);
            if (invalid is not null)
            {
                return invalid;
            }

            var type = await _db.ConsumableTypes.SingleOrDefaultAsync(candidate => candidate.ConsumableTypeId == consumableTypeId, token);
            if (type is null)
            {
                return ConfigurationOutcome.Failure("That type is not in the list.");
            }

            if (await TypeNameTakenAsync(trimmed, consumableTypeId, token))
            {
                return ConfigurationOutcome.Failure(TypeNameTaken);
            }

            // Section 4.5(c): every lot of every consumable of the type must fit the new stock mode.
            if (!string.Equals(type.StockMode, stockMode, StringComparison.Ordinal))
            {
                var lots = _db.PrintingStockEntries.Where(entry => entry.Quantity > 0 && entry.PrintingConsumable.ConsumableTypeId == consumableTypeId);
                var conflict = stockMode switch
                {
                    ConsumableStockModes.Undated => await lots.AnyAsync(entry => entry.ExpirationDate != null, token),
                    ConsumableStockModes.DateRequired => await lots.AnyAsync(entry => entry.ExpirationDate == null, token),
                    _ => false
                };
                if (conflict)
                {
                    return ConfigurationOutcome.Failure("This type has stock that does not fit that rule.");
                }

                var hasExpiration = stockMode != ConsumableStockModes.Undated;
                await _db.PrintingConsumables
                    .Where(item => item.ConsumableTypeId == consumableTypeId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.HasExpirationDate, hasExpiration), token);
            }

            type.Name = trimmed;
            type.StockMode = stockMode!;
            type.AcknowledgeEarliestExpiration = acknowledgeEarliestExpiration;
            type.UsesPrinterCompatibility = usesPrinterCompatibility;
            type.DisplayOrder = displayOrder;
            var saved = await SaveAsync(TypeNameTaken, token);
            if (saved.Completed)
            {
                _logger.LogInformation("Operator updated consumable type {ConsumableType}.", trimmed);
            }

            return saved;
        }, cancellationToken);

    public Task<ConfigurationOutcome> SetConsumableTypeActiveAsync(int consumableTypeId, bool active, CancellationToken cancellationToken) =>
        SerializableAsync(async token =>
        {
            var type = await _db.ConsumableTypes.SingleOrDefaultAsync(candidate => candidate.ConsumableTypeId == consumableTypeId, token);
            if (type is null)
            {
                return ConfigurationOutcome.Failure("That type is not in the list.");
            }

            // Section 4.5(e): no active material and no stock on any material of the type, active or inactive (D6).
            if (!active)
            {
                var activeMaterial = await _db.PrintingConsumables.AnyAsync(item => item.ConsumableTypeId == consumableTypeId && item.Active, token);
                var stock = await _db.PrintingStockEntries.AnyAsync(
                    entry => entry.Quantity > 0 && entry.PrintingConsumable.ConsumableTypeId == consumableTypeId,
                    token);
                if (activeMaterial || stock)
                {
                    return ConfigurationOutcome.Failure("This type still has active materials or stock.");
                }
            }

            type.Active = active;
            await _db.SaveChangesAsync(token);
            _logger.LogInformation("Operator set consumable type {ConsumableType} active to {Active}.", type.Name, active);
            return ConfigurationOutcome.Success();
        }, cancellationToken);

    public async Task<ConfigurationOutcome> DeleteConsumableTypeAsync(int consumableTypeId, CancellationToken cancellationToken)
    {
        var type = await _db.ConsumableTypes.SingleOrDefaultAsync(candidate => candidate.ConsumableTypeId == consumableTypeId, cancellationToken);
        if (type is null)
        {
            return ConfigurationOutcome.Failure("That type is not in the list.");
        }

        const string used = "This type is used by materials. Deactivate it instead.";
        if (await _db.PrintingConsumables.AnyAsync(item => item.ConsumableTypeId == consumableTypeId, cancellationToken))
        {
            return ConfigurationOutcome.Failure(used);
        }

        var name = type.Name;
        var deleted = await DeleteAsync(type, used, cancellationToken);
        if (deleted.Completed)
        {
            _logger.LogInformation("Operator deleted consumable type {ConsumableType}.", name);
        }

        return deleted;
    }

    /// <summary>Section 4.1: 1 to 64 characters after trimming, no control characters.</summary>
    public static string? CheckTypeName(string? name, out string trimmed)
    {
        trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return "Enter a type name.";
        }

        if (trimmed.Length > ConsumableTypeConfiguration.NameMaxLength)
        {
            return "Enter a shorter type name.";
        }

        return trimmed.Any(char.IsControl) ? "Remove line breaks and tabs from the type name." : null;
    }

    private static ConfigurationOutcome? CheckType(string? name, string? stockMode, bool acknowledge, int displayOrder, out string trimmed)
    {
        var nameError = CheckTypeName(name, out trimmed);
        if (nameError is not null)
        {
            return ConfigurationOutcome.Failure(nameError);
        }

        if (!ConsumableStockModes.IsValid(stockMode))
        {
            return ConfigurationOutcome.Failure("Choose how the stock of this type is dated.");
        }

        if (acknowledge && stockMode == ConsumableStockModes.Undated)
        {
            return ConfigurationOutcome.Failure("Earliest-date acknowledgement needs an expiration date.");
        }

        return displayOrder < 0 ? ConfigurationOutcome.Failure("Enter an order of zero or more.") : null;
    }

    /// <summary>
    /// Section 4.1a: the database decides. The same expression as the computed NameKey column; the unique index on NameKey
    /// is the final guarantee, and its violation is mapped to the same message by SaveAsync.
    /// </summary>
    private async Task<bool> TypeNameTakenAsync(string name, int? exceptTypeId, CancellationToken cancellationToken) =>
        await _db.ConsumableTypes
            .FromSqlInterpolated($"SELECT * FROM [ConsumableTypes] WHERE [NameKey] = UPPER({name}) COLLATE Latin1_General_100_BIN2")
            .AnyAsync(type => exceptTypeId == null || type.ConsumableTypeId != exceptTypeId, cancellationToken);

    // ---- Consumables ----

    public async Task<ConfigurationOutcome> AddConsumableAsync(int? consumableTypeId, string? name, string? code, string? capacity, CancellationToken cancellationToken)
    {
        var type = consumableTypeId is int id
            ? await _db.ConsumableTypes.SingleOrDefaultAsync(candidate => candidate.ConsumableTypeId == id, cancellationToken)
            : null;
        if (type is null || !type.Active)
        {
            return ConfigurationOutcome.Failure("Choose a type.");
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
            .Select(item => new { item.ConsumableTypeId, item.Name, item.Code })
            .ToListAsync(cancellationToken);
        if (existing.Any(item => item.ConsumableTypeId == type.ConsumableTypeId && string.Equals(item.Name.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return ConfigurationOutcome.Failure("That material is already in this category.");
        }

        if (trimmedCode is not null && existing.Any(item => string.Equals(item.Code, trimmedCode, StringComparison.Ordinal)))
        {
            return ConfigurationOutcome.Failure("That code is already registered.");
        }

        _db.PrintingConsumables.Add(new PrintingConsumable
        {
            ConsumableTypeId = type.ConsumableTypeId,
            Name = trimmedName,
            Code = trimmedCode,
            Capacity = trimmedCapacity,
            HasExpirationDate = type.StockMode != ConsumableStockModes.Undated,
            LowStockThreshold = 0,
            CriticalStockThreshold = 0,
            Status = ConsumableStock.Depleted,
            Active = true
        });
        var saved = await SaveAsync("That code is already registered.", cancellationToken);
        if (saved.Completed)
        {
            _logger.LogInformation("Operator added {ConsumableType} {Consumable}.", type.Name, trimmedCode ?? trimmedName);
        }

        return saved;
    }

    public Task<ConfigurationOutcome> UpdateConsumableAsync(
        int printingConsumableId,
        int? consumableTypeId,
        string? name,
        string? code,
        string? capacity,
        int lowStockThreshold,
        int criticalStockThreshold,
        CancellationToken cancellationToken) =>
        SerializableAsync(async token =>
        {
            if (consumableTypeId is null)
            {
                return ConfigurationOutcome.Failure("Choose a type.");
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
                .SingleOrDefaultAsync(candidate => candidate.PrintingConsumableId == printingConsumableId, token);
            if (item is null)
            {
                return ConfigurationOutcome.Failure("That consumable is not in the list.");
            }

            var type = await _db.ConsumableTypes.SingleOrDefaultAsync(candidate => candidate.ConsumableTypeId == consumableTypeId, token);
            if (type is null)
            {
                return ConfigurationOutcome.Failure("Choose a type.");
            }

            // Section 4.6: the type changes only while there is no stock entry, and only to an active type.
            if (item.ConsumableTypeId != type.ConsumableTypeId)
            {
                if (item.StockEntries.Count > 0)
                {
                    return ConfigurationOutcome.Failure("This material has stock, so its category stays the same.");
                }

                if (!type.Active)
                {
                    return ConfigurationOutcome.Failure("Choose an active type.");
                }
            }

            var existing = await _db.PrintingConsumables
                .Where(candidate => candidate.PrintingConsumableId != printingConsumableId)
                .Select(candidate => new { candidate.ConsumableTypeId, candidate.Name, candidate.Code })
                .ToListAsync(token);
            if (existing.Any(candidate => candidate.ConsumableTypeId == type.ConsumableTypeId && string.Equals(candidate.Name.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase)))
            {
                return ConfigurationOutcome.Failure("That material is already in this category.");
            }

            if (trimmedCode is not null && existing.Any(candidate => string.Equals(candidate.Code, trimmedCode, StringComparison.Ordinal)))
            {
                return ConfigurationOutcome.Failure("That code is already registered.");
            }

            item.ConsumableTypeId = type.ConsumableTypeId;
            item.Name = trimmedName;
            item.Code = trimmedCode;
            item.Capacity = trimmedCapacity;
            item.HasExpirationDate = type.StockMode != ConsumableStockModes.Undated;
            item.LowStockThreshold = lowStockThreshold;
            item.CriticalStockThreshold = criticalStockThreshold;
            var quantity = item.StockEntries.Where(entry => entry.Quantity > 0).Sum(entry => entry.Quantity);
            item.Status = ConsumableStock.FromQuantity(quantity, lowStockThreshold, criticalStockThreshold);
            var saved = await SaveAsync("That code is already registered.", token);
            if (saved.Completed)
            {
                _logger.LogInformation("Operator updated {ConsumableType} {Consumable}.", type.Name, trimmedCode ?? trimmedName);
            }

            return saved;
        }, cancellationToken);

    public Task<ConfigurationOutcome> SetConsumableActiveAsync(int printingConsumableId, bool active, CancellationToken cancellationToken) =>
        SerializableAsync(async token =>
        {
            var item = await _db.PrintingConsumables
                .Include(candidate => candidate.ConsumableType)
                .SingleOrDefaultAsync(candidate => candidate.PrintingConsumableId == printingConsumableId, token);
            if (item is null)
            {
                return ConfigurationOutcome.Failure("That consumable is not in the list.");
            }

            // Section 4.7(c): a material of an inactive type cannot be reactivated. Deactivation keeps today's rule (D6).
            if (active && !item.ConsumableType.Active)
            {
                return ConfigurationOutcome.Failure("Activate the type of this material first.");
            }

            item.Active = active;
            await _db.SaveChangesAsync(token);
            _logger.LogInformation("Operator set consumable {Consumable} active to {Active}.", item.Code ?? item.Name, active);
            return ConfigurationOutcome.Success();
        }, cancellationToken);

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

        // Section 7.8.
        const string history = "This material has stock history. Deactivate it instead.";
        if (await _db.PrintingStockMovements.AnyAsync(movement => movement.PrintingConsumableId == printingConsumableId, cancellationToken))
        {
            return ConfigurationOutcome.Failure(history);
        }

        if (await _db.PrinterModelConsumables.AnyAsync(link => link.PrintingConsumableId == printingConsumableId, cancellationToken))
        {
            return ConfigurationOutcome.Failure("This material cannot be deleted because a printer model is associated with it.");
        }

        var name = item.Code ?? item.Name;
        var deleted = await DeleteAsync(item, history, cancellationToken);
        if (deleted.Completed)
        {
            _logger.LogInformation("Operator deleted consumable {Consumable}.", name);
        }

        return deleted;
    }

    /// <summary>Runs a check-then-write rule of section 4 in one serializable transaction, so that two saves cannot both pass it.</summary>
    private async Task<ConfigurationOutcome> SerializableAsync(Func<CancellationToken, Task<ConfigurationOutcome>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var outcome = await action(cancellationToken);
            if (outcome.Completed)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return outcome;
        }
        catch (Exception exception) when (IsDeadlock(exception))
        {
            _db.ChangeTracker.Clear();
            return ConfigurationOutcome.Failure("The configuration changed while this was saved. Try again.");
        }
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

        var consumable = await _db.PrintingConsumables
            .Where(item => item.PrintingConsumableId == printingConsumableId)
            .Select(item => new { item.ConsumableType.UsesPrinterCompatibility })
            .SingleOrDefaultAsync(cancellationToken);
        if (consumable is null)
        {
            return ConfigurationOutcome.Failure("That consumable is not in the list.");
        }

        // Contract section 4.3: a type without printer compatibility offers no new link; existing links can still be removed.
        if (compatible && !consumable.UsesPrinterCompatibility)
        {
            return ConfigurationOutcome.Failure("This type does not use printer compatibility.");
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
