using System.Globalization;
using Microsoft.AspNetCore.Components;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

/// <summary>
/// Shared state and operations of the three Operator Configuration windows (Operator Configuration
/// Presentation Contract v1, sections 5 to 7). Each window renders only its own two panels; every
/// operation goes through IOperatorConfiguration exactly as before.
/// </summary>
public abstract class ConfigurationWindow : ComponentBase
{
    [Inject]
    protected IOperatorConfiguration Configuration { get; set; } = null!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = null!;

    protected OperatorConfigurationView View { get; set; } = EmptyView();

    protected string NewDepartment { get; set; } = "";

    protected string NewReason { get; set; } = "";

    protected bool NewReasonRequiresMentor { get; set; }

    protected bool NewReasonRequiresApprovalSheet { get; set; }

    protected string NewPrinter { get; set; } = "";

    protected string NewCategory { get; set; } = "";

    protected string NewTypeName { get; set; } = "";

    protected string NewTypeMode { get; set; } = PosterPrintRequest.Domain.ConsumableStockModes.DateRequired;

    protected string NewTypeOrder { get; set; } = "0";

    protected bool NewTypeAcknowledge { get; set; }

    protected bool NewTypePrinters { get; set; } = true;

    protected string TypeStatus { get; set; } = ConfigurationPresentation.Active;

    protected int? EditingTypeId { get; set; }

    protected string TypeEditName { get; set; } = "";

    protected string TypeEditMode { get; set; } = "";

    protected string TypeEditOrder { get; set; } = "0";

    protected bool TypeEditAcknowledge { get; set; }

    protected bool TypeEditPrinters { get; set; }

    protected string? TypeError { get; set; }

    protected static readonly (string Value, string Label)[] StockModeChoices =
    [
        (PosterPrintRequest.Domain.ConsumableStockModes.DateRequired, "Required"),
        (PosterPrintRequest.Domain.ConsumableStockModes.DateOptional, "Optional"),
        (PosterPrintRequest.Domain.ConsumableStockModes.Undated, "None")
    ];

    protected IReadOnlyList<ConfiguredConsumableType> VisibleTypes =>
        ConfigurationPresentation.Apply(View.ConsumableTypes, TypeStatus, string.Empty, type => type.Name, type => type.Active);

    protected IEnumerable<ConfiguredConsumableType> ActiveTypes => View.ConsumableTypes.Where(type => type.Active);

    protected string NewConsumableName { get; set; } = "";

    protected string NewConsumableCode { get; set; } = "";

    protected string NewConsumableCapacity { get; set; } = "";

    protected string DepartmentStatus { get; set; } = ConfigurationPresentation.Active;

    protected string DepartmentSearch { get; set; } = "";

    protected string ReasonStatus { get; set; } = ConfigurationPresentation.Active;

    protected string ReasonSearch { get; set; } = "";

    protected string PrinterStatus { get; set; } = ConfigurationPresentation.Active;

    protected string PrinterSearch { get; set; } = "";

    protected string ConsumableStatus { get; set; } = ConfigurationPresentation.Active;

    protected string ConsumableSearch { get; set; } = "";

    protected int? EditingDepartmentId { get; set; }

    protected string DepartmentEditName { get; set; } = "";

    protected int? EditingReasonId { get; set; }

    protected string ReasonEditName { get; set; } = "";

    protected bool ReasonEditMentor { get; set; }

    protected bool ReasonEditApproval { get; set; }

    protected int? EditingPrinterId { get; set; }

    protected string PrinterEditName { get; set; } = "";

    protected int? EditingConsumableId { get; set; }

    protected string ConsumableEditCategory { get; set; } = "";

    protected string ConsumableEditName { get; set; } = "";

    protected string ConsumableEditCode { get; set; } = "";

    protected string ConsumableEditCapacity { get; set; } = "";

    protected string ConsumableEditCritical { get; set; } = "0";

    protected string ConsumableEditLow { get; set; } = "0";

    protected string? DepartmentError { get; set; }

    protected string? ReasonError { get; set; }

    protected string? PrinterError { get; set; }

    protected string? ConsumableError { get; set; }

    protected bool Busy { get; set; }

    protected IReadOnlyList<ConfiguredValue> VisibleDepartments =>
        ConfigurationPresentation.Apply(View.Departments, DepartmentStatus, DepartmentSearch, department => department.Name, department => department.Active);

    protected IReadOnlyList<ConfiguredReason> VisibleReasons =>
        ConfigurationPresentation.Apply(View.Reasons, ReasonStatus, ReasonSearch, reason => reason.Name, reason => reason.Active);

    protected IReadOnlyList<ConfiguredPrinterModel> VisiblePrinters =>
        ConfigurationPresentation.Apply(View.PrinterModels, PrinterStatus, PrinterSearch, model => model.Name, model => model.Active);

    protected IReadOnlyList<ConfiguredConsumable> VisibleConsumables =>
        ConfigurationPresentation.Apply(View.Consumables, ConsumableStatus, ConsumableSearch, ConsumableSearchText, consumable => consumable.Active);

    protected IEnumerable<ConfiguredPrinterModel> ActivePrinters => View.PrinterModels.Where(model => model.Active);

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    protected void OnDepartmentInput(ChangeEventArgs args) => NewDepartment = args.Value?.ToString() ?? "";

    protected void OnDepartmentEditInput(ChangeEventArgs args) => DepartmentEditName = args.Value?.ToString() ?? "";

    protected void OnReasonInput(ChangeEventArgs args) => NewReason = args.Value?.ToString() ?? "";

    protected void OnReasonEditInput(ChangeEventArgs args) => ReasonEditName = args.Value?.ToString() ?? "";

    protected void OnReasonMentorChanged(ChangeEventArgs args) =>
        NewReasonRequiresMentor = args.Value is bool selected && selected;

    protected void OnReasonApprovalChanged(ChangeEventArgs args) =>
        NewReasonRequiresApprovalSheet = args.Value is bool selected && selected;

    protected void OnPrinterInput(ChangeEventArgs args) => NewPrinter = args.Value?.ToString() ?? "";

    protected void OnPrinterEditInput(ChangeEventArgs args) => PrinterEditName = args.Value?.ToString() ?? "";

    protected void OnCategoryChanged(ChangeEventArgs args) => NewCategory = args.Value?.ToString() ?? "";

    protected void OnConsumableNameInput(ChangeEventArgs args) => NewConsumableName = args.Value?.ToString() ?? "";

    protected void OnConsumableCodeInput(ChangeEventArgs args) => NewConsumableCode = args.Value?.ToString() ?? "";

    protected void OnConsumableCapacityInput(ChangeEventArgs args) => NewConsumableCapacity = args.Value?.ToString() ?? "";

    protected async Task AddDepartmentAsync()
    {
        DepartmentError = null;
        await RunAsync(async () =>
        {
            var outcome = await Configuration.AddDepartmentAsync(NewDepartment, CancellationToken.None);
            DepartmentError = outcome.Message;
            if (outcome.Completed)
            {
                NewDepartment = "";
            }
        });
    }

    protected void BeginDepartmentEdit(ConfiguredValue department)
    {
        DepartmentError = null;
        EditingDepartmentId = department.Id;
        DepartmentEditName = department.Name;
    }

    protected async Task SaveDepartmentAsync(int departmentId)
    {
        DepartmentError = null;
        await RunAsync(async () =>
        {
            var outcome = await Configuration.UpdateDepartmentAsync(departmentId, DepartmentEditName, CancellationToken.None);
            DepartmentError = outcome.Message;
            if (outcome.Completed)
            {
                EditingDepartmentId = null;
                DepartmentEditName = "";
            }
        });
    }

    protected async Task DeleteDepartmentAsync(int departmentId)
    {
        DepartmentError = null;
        await RunAsync(async () =>
        {
            var outcome = await Configuration.DeleteDepartmentAsync(departmentId, CancellationToken.None);
            DepartmentError = outcome.Message;
            if (outcome.Completed && EditingDepartmentId == departmentId)
            {
                EditingDepartmentId = null;
                DepartmentEditName = "";
            }
        });
    }

    protected async Task AddReasonAsync()
    {
        ReasonError = null;
        await RunAsync(async () =>
        {
            var outcome = await Configuration.AddReasonAsync(NewReason, NewReasonRequiresMentor, NewReasonRequiresApprovalSheet, CancellationToken.None);
            ReasonError = outcome.Message;
            if (outcome.Completed)
            {
                NewReason = "";
                NewReasonRequiresMentor = false;
                NewReasonRequiresApprovalSheet = false;
            }
        });
    }

    protected void BeginReasonEdit(ConfiguredReason reason)
    {
        ReasonError = null;
        EditingReasonId = reason.Id;
        ReasonEditName = reason.Name;
        ReasonEditMentor = reason.RequiresMentor;
        ReasonEditApproval = reason.RequiresApprovalSheet;
    }

    protected async Task SaveReasonAsync(int reasonId)
    {
        ReasonError = null;
        await RunAsync(async () =>
        {
            var outcome = await Configuration.UpdateReasonAsync(reasonId, ReasonEditName, ReasonEditMentor, ReasonEditApproval, CancellationToken.None);
            ReasonError = outcome.Message;
            if (outcome.Completed)
            {
                EditingReasonId = null;
                ReasonEditName = "";
            }
        });
    }

    protected async Task DeleteReasonAsync(int reasonId)
    {
        ReasonError = null;
        await RunAsync(async () =>
        {
            var outcome = await Configuration.DeleteReasonAsync(reasonId, CancellationToken.None);
            ReasonError = outcome.Message;
            if (outcome.Completed && EditingReasonId == reasonId)
            {
                EditingReasonId = null;
                ReasonEditName = "";
            }
        });
    }

    protected async Task AddPrinterAsync()
    {
        PrinterError = null;
        await RunAsync(async () =>
        {
            var outcome = await Configuration.AddPrinterModelAsync(NewPrinter, CancellationToken.None);
            PrinterError = outcome.Message;
            if (outcome.Completed)
            {
                NewPrinter = "";
            }
        });
    }

    protected void BeginPrinterEdit(ConfiguredPrinterModel model)
    {
        PrinterError = null;
        EditingPrinterId = model.Id;
        PrinterEditName = model.Name;
    }

    protected async Task SavePrinterAsync(int printerModelId)
    {
        PrinterError = null;
        await RunAsync(async () =>
        {
            var outcome = await Configuration.UpdatePrinterModelAsync(printerModelId, PrinterEditName, CancellationToken.None);
            PrinterError = outcome.Message;
            if (outcome.Completed)
            {
                EditingPrinterId = null;
                PrinterEditName = "";
            }
        });
    }

    protected async Task DeletePrinterAsync(int printerModelId)
    {
        PrinterError = null;
        await RunAsync(async () =>
        {
            var outcome = await Configuration.DeletePrinterModelAsync(printerModelId, CancellationToken.None);
            PrinterError = outcome.Message;
            if (outcome.Completed && EditingPrinterId == printerModelId)
            {
                EditingPrinterId = null;
                PrinterEditName = "";
            }
        });
    }

    protected async Task AddConsumableAsync()
    {
        ConsumableError = null;
        await RunAsync(async () =>
        {
            var outcome = await Configuration.AddConsumableAsync(ParseId(NewCategory), NewConsumableName, NewConsumableCode, NewConsumableCapacity, CancellationToken.None);
            ConsumableError = outcome.Message;
            if (outcome.Completed)
            {
                NewCategory = "";
                NewConsumableName = "";
                NewConsumableCode = "";
                NewConsumableCapacity = "";
            }
        });
    }

    protected void BeginConsumableEdit(ConfiguredConsumable consumable)
    {
        ConsumableError = null;
        EditingConsumableId = consumable.Id;
        ConsumableEditCategory = consumable.ConsumableTypeId.ToString(CultureInfo.InvariantCulture);
        ConsumableEditName = consumable.Name;
        ConsumableEditCode = consumable.Code ?? "";
        ConsumableEditCapacity = consumable.Capacity ?? "";
        ConsumableEditCritical = consumable.CriticalStockThreshold.ToString(CultureInfo.InvariantCulture);
        ConsumableEditLow = consumable.LowStockThreshold.ToString(CultureInfo.InvariantCulture);
    }

    protected async Task SaveConsumableAsync(int printingConsumableId)
    {
        ConsumableError = null;
        if (!TryThreshold(ConsumableEditCritical, out var critical) || !TryThreshold(ConsumableEditLow, out var low))
        {
            ConsumableError = "Enter a threshold of zero or more.";
            return;
        }

        await RunAsync(async () =>
        {
            var outcome = await Configuration.UpdateConsumableAsync(
                printingConsumableId,
                ParseId(ConsumableEditCategory),
                ConsumableEditName,
                ConsumableEditCode,
                ConsumableEditCapacity,
                low,
                critical,
                CancellationToken.None);
            ConsumableError = outcome.Message;
            if (!outcome.Completed)
            {
                return;
            }

            EditingConsumableId = null;
        });
    }

    protected async Task AddTypeAsync()
    {
        TypeError = null;
        if (!TryThreshold(NewTypeOrder, out var order))
        {
            TypeError = "Enter an order of zero or more.";
            return;
        }

        await RunAsync(async () =>
        {
            var outcome = await Configuration.AddConsumableTypeAsync(NewTypeName, NewTypeMode, NewTypeAcknowledge, NewTypePrinters, order, CancellationToken.None);
            TypeError = outcome.Message;
            if (outcome.Completed)
            {
                NewTypeName = "";
                NewTypeOrder = "0";
                NewTypeAcknowledge = false;
                NewTypePrinters = true;
            }
        });
    }

    protected void BeginTypeEdit(ConfiguredConsumableType type)
    {
        TypeError = null;
        EditingTypeId = type.Id;
        TypeEditName = type.Name;
        TypeEditMode = type.StockMode;
        TypeEditOrder = type.DisplayOrder.ToString(CultureInfo.InvariantCulture);
        TypeEditAcknowledge = type.AcknowledgeEarliestExpiration;
        TypeEditPrinters = type.UsesPrinterCompatibility;
    }

    protected async Task SaveTypeAsync(int consumableTypeId)
    {
        TypeError = null;
        if (!TryThreshold(TypeEditOrder, out var order))
        {
            TypeError = "Enter an order of zero or more.";
            return;
        }

        await RunAsync(async () =>
        {
            var outcome = await Configuration.UpdateConsumableTypeAsync(consumableTypeId, TypeEditName, TypeEditMode, TypeEditAcknowledge, TypeEditPrinters, order, CancellationToken.None);
            TypeError = outcome.Message;
            if (outcome.Completed)
            {
                EditingTypeId = null;
            }
        });
    }

    protected async Task DeleteTypeAsync(int consumableTypeId)
    {
        TypeError = null;
        await RunAsync(async () =>
        {
            var outcome = await Configuration.DeleteConsumableTypeAsync(consumableTypeId, CancellationToken.None);
            TypeError = outcome.Message;
            if (outcome.Completed && EditingTypeId == consumableTypeId)
            {
                EditingTypeId = null;
            }
        });
    }

    protected Task SetTypeActiveAsync(int consumableTypeId, ChangeEventArgs args) =>
        SetFlagAsync(args, active => Configuration.SetConsumableTypeActiveAsync(consumableTypeId, active, CancellationToken.None), message => TypeError = message);

    protected static string TypeSummary(ConfiguredConsumableType type)
    {
        var mode = StockModeChoices.FirstOrDefault(choice => choice.Value == type.StockMode).Label ?? type.StockMode;
        var parts = new List<string>
        {
            "Expiration date: " + mode + ".",
            type.AcknowledgeEarliestExpiration ? "Confirms the first expiration." : "",
            type.UsesPrinterCompatibility ? "Printer compatibility." : "No printer compatibility.",
            "Order " + type.DisplayOrder.ToString(CultureInfo.InvariantCulture) + ".",
            type.ConsumableCount.ToString(CultureInfo.InvariantCulture) + (type.ConsumableCount == 1 ? " material." : " materials.")
        };
        return string.Join(" ", parts.Where(part => part.Length > 0));
    }

    protected static int? ParseId(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    protected static string TypeCheckId(int id) => "type-active-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string TypeEditId(int id) => "type-edit-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string TypeDeleteId(int id) => "type-delete-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string TypeFormId(int id) => "type-form-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string TypeNameId(int id) => "type-name-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string TypeModeId(int id) => "type-mode-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string TypeOrderId(int id) => "type-order-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string TypeAcknowledgeId(int id) => "type-acknowledge-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string TypePrintersId(int id) => "type-printers-" + id.ToString(CultureInfo.InvariantCulture);

    protected async Task DeleteConsumableAsync(int printingConsumableId)
    {
        ConsumableError = null;
        await RunAsync(async () =>
        {
            var outcome = await Configuration.DeleteConsumableAsync(printingConsumableId, CancellationToken.None);
            ConsumableError = outcome.Message;
            if (outcome.Completed && EditingConsumableId == printingConsumableId)
            {
                EditingConsumableId = null;
            }
        });
    }

    protected Task SetDepartmentActiveAsync(int departmentId, ChangeEventArgs args) =>
        SetFlagAsync(args, active => Configuration.SetDepartmentActiveAsync(departmentId, active, CancellationToken.None), message => DepartmentError = message);

    protected Task SetReasonActiveAsync(int reasonId, ChangeEventArgs args) =>
        SetFlagAsync(args, active => Configuration.SetReasonActiveAsync(reasonId, active, CancellationToken.None), message => ReasonError = message);

    protected Task SetPrinterActiveAsync(int printerModelId, ChangeEventArgs args) =>
        SetFlagAsync(args, active => Configuration.SetPrinterModelActiveAsync(printerModelId, active, CancellationToken.None), message => PrinterError = message);

    protected Task SetConsumableActiveAsync(int printingConsumableId, ChangeEventArgs args) =>
        SetFlagAsync(args, active => Configuration.SetConsumableActiveAsync(printingConsumableId, active, CancellationToken.None), message => ConsumableError = message);

    protected static bool TryThreshold(string value, out int threshold) =>
        int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out threshold) && threshold >= 0;

    protected async Task SetFlagAsync(
        ChangeEventArgs args,
        Func<bool, Task<ConfigurationOutcome>> save,
        Action<string?> assignError)
    {
        assignError(null);
        var selected = args.Value is bool value && value;
        await RunAsync(async () =>
        {
            var outcome = await save(selected);
            assignError(outcome.Message);
        });
    }

    protected async Task RunAsync(Func<Task> action)
    {
        if (Busy)
        {
            return;
        }

        Busy = true;
        try
        {
            await action();
            await ReloadAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    protected async Task ReloadAsync() => View = await Configuration.LoadAsync(CancellationToken.None);

    protected string CompatibilitySummary(ConfiguredConsumable consumable)
    {
        var names = View.PrinterModels
            .Where(model => model.Active && model.CompatibleConsumableIds.Contains(consumable.Id))
            .Select(model => model.Name)
            .ToArray();
        return names.Length == 0 ? "No printer selected." : string.Join(", ", names);
    }

    protected static string ConsumableSearchText(ConfiguredConsumable consumable) =>
        string.Join(" ", new[] { consumable.Name, consumable.Code, consumable.TypeName, consumable.Capacity }.Where(value => !string.IsNullOrWhiteSpace(value)));

    protected static string DepartmentCheckId(int id) => "department-active-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string DepartmentEditId(int id) => "department-edit-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string DepartmentDeleteId(int id) => "department-delete-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string DepartmentNameId(int id) => "department-name-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string DepartmentFormId(int id) => "department-form-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ReasonCheckId(int id) => "event-active-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ReasonEditId(int id) => "event-edit-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ReasonDeleteId(int id) => "event-delete-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ReasonNameId(int id) => "event-name-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ReasonFormId(int id) => "event-form-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ReasonMentorId(int id) => "event-mentor-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ReasonApprovalId(int id) => "event-approval-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string PrinterCheckId(int id) => "printer-active-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string PrinterEditId(int id) => "printer-edit-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string PrinterDeleteId(int id) => "printer-delete-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string PrinterNameId(int id) => "printer-name-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string PrinterFormId(int id) => "printer-form-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ConsumableCheckId(int id) => "consumable-active-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ConsumableEditId(int id) => "consumable-edit-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ConsumableDeleteId(int id) => "consumable-delete-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ConsumableFormId(int id) => "consumable-form-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ConsumableCategoryId(int id) => "consumable-category-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ConsumableNameId(int id) => "consumable-name-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ConsumableCodeId(int id) => "consumable-code-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ConsumableCapacityId(int id) => "consumable-capacity-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string CriticalInputId(int id) => "consumable-critical-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string LowInputId(int id) => "consumable-low-" + id.ToString(CultureInfo.InvariantCulture);

    protected static string ConsumableLabel(ConfiguredConsumable consumable)
    {
        var category = consumable.TypeName;
        var code = string.IsNullOrWhiteSpace(consumable.Code) ? "" : " " + consumable.Code;
        var capacity = string.IsNullOrWhiteSpace(consumable.Capacity) ? "" : " · " + consumable.Capacity;
        return category + " · " + consumable.Name + code + capacity;
    }

    protected static OperatorConfigurationView EmptyView() => new()
    {
        Departments = [],
        Reasons = [],
        PrinterModels = [],
        ConsumableTypes = [],
        Consumables = []
    };
}
