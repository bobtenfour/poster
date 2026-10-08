using System.Globalization;
using Microsoft.AspNetCore.Components;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

public partial class TechnicianConfiguration : ComponentBase
{
    [Inject]
    private IOperatorConfiguration Configuration { get; set; } = null!;

    private OperatorConfigurationView View { get; set; } = EmptyView();

    private string NewDepartment { get; set; } = "";

    private string NewReason { get; set; } = "";

    private bool NewReasonRequiresMentor { get; set; }

    private bool NewReasonRequiresApprovalSheet { get; set; }

    private string NewPrinter { get; set; } = "";

    private string NewCategory { get; set; } = "";

    private string NewConsumableName { get; set; } = "";

    private string NewConsumableCode { get; set; } = "";

    private string NewConsumableCapacity { get; set; } = "";

    private string DepartmentStatus { get; set; } = ConfigurationPresentation.Active;

    private string DepartmentSearch { get; set; } = "";

    private string ReasonStatus { get; set; } = ConfigurationPresentation.Active;

    private string ReasonSearch { get; set; } = "";

    private string PrinterStatus { get; set; } = ConfigurationPresentation.Active;

    private string PrinterSearch { get; set; } = "";

    private string ConsumableStatus { get; set; } = ConfigurationPresentation.Active;

    private string ConsumableSearch { get; set; } = "";

    private int? EditingDepartmentId { get; set; }

    private string DepartmentEditName { get; set; } = "";

    private int? EditingReasonId { get; set; }

    private string ReasonEditName { get; set; } = "";

    private bool ReasonEditMentor { get; set; }

    private bool ReasonEditApproval { get; set; }

    private int? EditingPrinterId { get; set; }

    private string PrinterEditName { get; set; } = "";

    private int? EditingConsumableId { get; set; }

    private string ConsumableEditCategory { get; set; } = "";

    private string ConsumableEditName { get; set; } = "";

    private string ConsumableEditCode { get; set; } = "";

    private string ConsumableEditCapacity { get; set; } = "";

    private string ConsumableEditCritical { get; set; } = "0";

    private string ConsumableEditLow { get; set; } = "0";

    private HashSet<int> ConsumableEditPrinters { get; set; } = [];

    private string? DepartmentError { get; set; }

    private string? ReasonError { get; set; }

    private string? PrinterError { get; set; }

    private string? ConsumableError { get; set; }

    private bool Busy { get; set; }

    private IReadOnlyList<ConfiguredValue> VisibleDepartments =>
        ConfigurationPresentation.Apply(View.Departments, DepartmentStatus, DepartmentSearch, department => department.Name, department => department.Active);

    private IReadOnlyList<ConfiguredReason> VisibleReasons =>
        ConfigurationPresentation.Apply(View.Reasons, ReasonStatus, ReasonSearch, reason => reason.Name, reason => reason.Active);

    private IReadOnlyList<ConfiguredPrinterModel> VisiblePrinters =>
        ConfigurationPresentation.Apply(View.PrinterModels, PrinterStatus, PrinterSearch, model => model.Name, model => model.Active);

    private IReadOnlyList<ConfiguredConsumable> VisibleConsumables =>
        ConfigurationPresentation.Apply(View.Consumables, ConsumableStatus, ConsumableSearch, ConsumableSearchText, consumable => consumable.Active);

    private IEnumerable<ConfiguredPrinterModel> ActivePrinters => View.PrinterModels.Where(model => model.Active);

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private void OnDepartmentInput(ChangeEventArgs args) => NewDepartment = args.Value?.ToString() ?? "";

    private void OnDepartmentEditInput(ChangeEventArgs args) => DepartmentEditName = args.Value?.ToString() ?? "";

    private void OnReasonInput(ChangeEventArgs args) => NewReason = args.Value?.ToString() ?? "";

    private void OnReasonEditInput(ChangeEventArgs args) => ReasonEditName = args.Value?.ToString() ?? "";

    private void OnReasonMentorChanged(ChangeEventArgs args) =>
        NewReasonRequiresMentor = args.Value is bool selected && selected;

    private void OnReasonApprovalChanged(ChangeEventArgs args) =>
        NewReasonRequiresApprovalSheet = args.Value is bool selected && selected;

    private void OnPrinterInput(ChangeEventArgs args) => NewPrinter = args.Value?.ToString() ?? "";

    private void OnPrinterEditInput(ChangeEventArgs args) => PrinterEditName = args.Value?.ToString() ?? "";

    private void OnCategoryChanged(ChangeEventArgs args) => NewCategory = args.Value?.ToString() ?? "";

    private void OnConsumableNameInput(ChangeEventArgs args) => NewConsumableName = args.Value?.ToString() ?? "";

    private void OnConsumableCodeInput(ChangeEventArgs args) => NewConsumableCode = args.Value?.ToString() ?? "";

    private void OnConsumableCapacityInput(ChangeEventArgs args) => NewConsumableCapacity = args.Value?.ToString() ?? "";

    private void TogglePrinter(int printerModelId, ChangeEventArgs args)
    {
        if (args.Value is bool selected && selected)
        {
            ConsumableEditPrinters.Add(printerModelId);
        }
        else
        {
            ConsumableEditPrinters.Remove(printerModelId);
        }
    }

    private async Task AddDepartmentAsync()
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

    private void BeginDepartmentEdit(ConfiguredValue department)
    {
        DepartmentError = null;
        EditingDepartmentId = department.Id;
        DepartmentEditName = department.Name;
    }

    private async Task SaveDepartmentAsync(int departmentId)
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

    private async Task DeleteDepartmentAsync(int departmentId)
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

    private async Task AddReasonAsync()
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

    private void BeginReasonEdit(ConfiguredReason reason)
    {
        ReasonError = null;
        EditingReasonId = reason.Id;
        ReasonEditName = reason.Name;
        ReasonEditMentor = reason.RequiresMentor;
        ReasonEditApproval = reason.RequiresApprovalSheet;
    }

    private async Task SaveReasonAsync(int reasonId)
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

    private async Task DeleteReasonAsync(int reasonId)
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

    private async Task AddPrinterAsync()
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

    private void BeginPrinterEdit(ConfiguredPrinterModel model)
    {
        PrinterError = null;
        EditingPrinterId = model.Id;
        PrinterEditName = model.Name;
    }

    private async Task SavePrinterAsync(int printerModelId)
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

    private async Task DeletePrinterAsync(int printerModelId)
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

    private async Task AddConsumableAsync()
    {
        ConsumableError = null;
        await RunAsync(async () =>
        {
            var outcome = await Configuration.AddConsumableAsync(NewCategory, NewConsumableName, NewConsumableCode, NewConsumableCapacity, CancellationToken.None);
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

    private void BeginConsumableEdit(ConfiguredConsumable consumable)
    {
        ConsumableError = null;
        EditingConsumableId = consumable.Id;
        ConsumableEditCategory = consumable.Category;
        ConsumableEditName = consumable.Name;
        ConsumableEditCode = consumable.Code ?? "";
        ConsumableEditCapacity = consumable.Capacity ?? "";
        ConsumableEditCritical = consumable.CriticalStockThreshold.ToString(CultureInfo.InvariantCulture);
        ConsumableEditLow = consumable.LowStockThreshold.ToString(CultureInfo.InvariantCulture);
        ConsumableEditPrinters = View.PrinterModels
            .Where(model => model.Active && model.CompatibleConsumableIds.Contains(consumable.Id))
            .Select(model => model.Id)
            .ToHashSet();
    }

    private async Task SaveConsumableAsync(int printingConsumableId)
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
                ConsumableEditCategory,
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

            foreach (var printer in View.PrinterModels.Where(model => model.Active))
            {
                var compatible = ConsumableEditPrinters.Contains(printer.Id);
                var current = printer.CompatibleConsumableIds.Contains(printingConsumableId);
                if (compatible == current)
                {
                    continue;
                }

                var link = await Configuration.SetCompatibilityAsync(printer.Id, printingConsumableId, compatible, CancellationToken.None);
                if (!link.Completed)
                {
                    ConsumableError = link.Message;
                    return;
                }
            }

            EditingConsumableId = null;
        });
    }

    private async Task DeleteConsumableAsync(int printingConsumableId)
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

    private Task SetDepartmentActiveAsync(int departmentId, ChangeEventArgs args) =>
        SetFlagAsync(args, active => Configuration.SetDepartmentActiveAsync(departmentId, active, CancellationToken.None), message => DepartmentError = message);

    private Task SetReasonActiveAsync(int reasonId, ChangeEventArgs args) =>
        SetFlagAsync(args, active => Configuration.SetReasonActiveAsync(reasonId, active, CancellationToken.None), message => ReasonError = message);

    private Task SetPrinterActiveAsync(int printerModelId, ChangeEventArgs args) =>
        SetFlagAsync(args, active => Configuration.SetPrinterModelActiveAsync(printerModelId, active, CancellationToken.None), message => PrinterError = message);

    private Task SetConsumableActiveAsync(int printingConsumableId, ChangeEventArgs args) =>
        SetFlagAsync(args, active => Configuration.SetConsumableActiveAsync(printingConsumableId, active, CancellationToken.None), message => ConsumableError = message);

    private static bool TryThreshold(string value, out int threshold) =>
        int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out threshold) && threshold >= 0;

    private async Task SetFlagAsync(
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

    private async Task RunAsync(Func<Task> action)
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

    private async Task ReloadAsync() => View = await Configuration.LoadAsync(CancellationToken.None);

    private string CompatibilitySummary(ConfiguredConsumable consumable)
    {
        var names = View.PrinterModels
            .Where(model => model.Active && model.CompatibleConsumableIds.Contains(consumable.Id))
            .Select(model => model.Name)
            .ToArray();
        return names.Length == 0 ? "No printer selected." : string.Join(", ", names);
    }

    private static string ConsumableSearchText(ConfiguredConsumable consumable) =>
        string.Join(" ", new[] { consumable.Name, consumable.Code, consumable.Category, consumable.Capacity }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string DepartmentCheckId(int id) => "department-active-" + id.ToString(CultureInfo.InvariantCulture);

    private static string DepartmentEditId(int id) => "department-edit-" + id.ToString(CultureInfo.InvariantCulture);

    private static string DepartmentDeleteId(int id) => "department-delete-" + id.ToString(CultureInfo.InvariantCulture);

    private static string DepartmentNameId(int id) => "department-name-" + id.ToString(CultureInfo.InvariantCulture);

    private static string DepartmentFormId(int id) => "department-form-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ReasonCheckId(int id) => "event-active-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ReasonEditId(int id) => "event-edit-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ReasonDeleteId(int id) => "event-delete-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ReasonNameId(int id) => "event-name-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ReasonFormId(int id) => "event-form-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ReasonMentorId(int id) => "event-mentor-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ReasonApprovalId(int id) => "event-approval-" + id.ToString(CultureInfo.InvariantCulture);

    private static string PrinterCheckId(int id) => "printer-active-" + id.ToString(CultureInfo.InvariantCulture);

    private static string PrinterEditId(int id) => "printer-edit-" + id.ToString(CultureInfo.InvariantCulture);

    private static string PrinterDeleteId(int id) => "printer-delete-" + id.ToString(CultureInfo.InvariantCulture);

    private static string PrinterNameId(int id) => "printer-name-" + id.ToString(CultureInfo.InvariantCulture);

    private static string PrinterFormId(int id) => "printer-form-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ConsumableCheckId(int id) => "consumable-active-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ConsumableEditId(int id) => "consumable-edit-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ConsumableDeleteId(int id) => "consumable-delete-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ConsumableFormId(int id) => "consumable-form-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ConsumableCategoryId(int id) => "consumable-category-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ConsumableNameId(int id) => "consumable-name-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ConsumableCodeId(int id) => "consumable-code-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ConsumableCapacityId(int id) => "consumable-capacity-" + id.ToString(CultureInfo.InvariantCulture);

    private static string CriticalInputId(int id) => "consumable-critical-" + id.ToString(CultureInfo.InvariantCulture);

    private static string LowInputId(int id) => "consumable-low-" + id.ToString(CultureInfo.InvariantCulture);

    private static string CompatibilityCheckId(int id) => "compatibility-" + id.ToString(CultureInfo.InvariantCulture);

    private static string ConsumableLabel(ConfiguredConsumable consumable)
    {
        var category = consumable.Category switch
        {
            ConsumableCategory.Cartridge => "Cartridge",
            ConsumableCategory.Paper => "Paper",
            ConsumableCategory.Laminating => "Laminating material",
            _ => consumable.Category
        };
        var code = string.IsNullOrWhiteSpace(consumable.Code) ? "" : " " + consumable.Code;
        var capacity = string.IsNullOrWhiteSpace(consumable.Capacity) ? "" : " · " + consumable.Capacity;
        return category + " · " + consumable.Name + code + capacity;
    }

    private static OperatorConfigurationView EmptyView() => new()
    {
        Departments = [],
        Reasons = [],
        PrinterModels = [],
        Consumables = []
    };
}
