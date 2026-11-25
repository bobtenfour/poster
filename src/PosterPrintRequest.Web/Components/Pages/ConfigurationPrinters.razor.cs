using System.Globalization;
using Microsoft.AspNetCore.Components;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

/// <summary>
/// Printer compatibility edited per printer model (Operator Configuration Presentation Contract v1, sections 6.2 to 6.4).
/// Changes go through the existing OperatorConfiguration.SetCompatibilityAsync, one call per changed pair.
/// </summary>
public partial class ConfigurationPrinters
{
    private int? CompatibilityPrinterId { get; set; }

    private string CompatibilityStatus { get; set; } = ConfigurationPresentation.Active;

    private HashSet<int> CompatibilitySelection { get; set; } = [];

    private string? CompatibilityError { get; set; }

    private string CompatibilityPrinterValue =>
        CompatibilityPrinterId?.ToString(CultureInfo.InvariantCulture) ?? "";

    private ConfiguredPrinterModel? CompatibilityPrinter =>
        CompatibilityPrinterId is int id ? View.PrinterModels.SingleOrDefault(model => model.Id == id && model.Active) : null;

    /// <summary>
    /// Consumables by type in DisplayOrder under the Show filter. A consumable whose type does not use printer
    /// compatibility is listed only while its stored link exists (Consumables Inventory Contract 4.3).
    /// </summary>
    private IReadOnlyList<CompatibilityGroup> CompatibilityGroups
    {
        get
        {
            var printer = CompatibilityPrinter;
            if (printer is null)
            {
                return [];
            }

            var visible = ConfigurationPresentation.Apply(View.Consumables, CompatibilityStatus, string.Empty, consumable => consumable.Name, consumable => consumable.Active)
                .Where(consumable => consumable.UsesPrinterCompatibility || printer.CompatibleConsumableIds.Contains(consumable.Id))
                .ToArray();
            return View.ConsumableTypes
                .OrderBy(type => type.DisplayOrder)
                .ThenBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
                .Select(type => new CompatibilityGroup(type, visible.Where(consumable => consumable.ConsumableTypeId == type.Id).ToArray()))
                .Where(group => group.Consumables.Count > 0)
                .ToArray();
        }
    }

    private void OnCompatibilityPrinterChanged(ChangeEventArgs args)
    {
        CompatibilityError = null;
        CompatibilityPrinterId = int.TryParse(args.Value?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
        ResetCompatibilitySelection();
    }

    private void ToggleCompatibility(int printingConsumableId, ChangeEventArgs args)
    {
        if (args.Value is bool selected && selected)
        {
            CompatibilitySelection.Add(printingConsumableId);
        }
        else
        {
            CompatibilitySelection.Remove(printingConsumableId);
        }
    }

    private async Task SaveCompatibilityAsync()
    {
        CompatibilityError = null;
        var printer = CompatibilityPrinter;
        if (printer is null)
        {
            CompatibilityError = "Choose a printer model.";
            return;
        }

        await RunAsync(async () =>
        {
            foreach (var consumable in CompatibilityGroups.SelectMany(group => group.Consumables))
            {
                var compatible = CompatibilitySelection.Contains(consumable.Id);
                if (compatible == printer.CompatibleConsumableIds.Contains(consumable.Id))
                {
                    continue;
                }

                var outcome = await Configuration.SetCompatibilityAsync(printer.Id, consumable.Id, compatible, CancellationToken.None);
                CompatibilityError = outcome.Message;
                if (!outcome.Completed)
                {
                    return;
                }
            }
        });
        ResetCompatibilitySelection();
    }

    private void ResetCompatibilitySelection() =>
        CompatibilitySelection = CompatibilityPrinter?.CompatibleConsumableIds.ToHashSet() ?? [];

    private static string CompatibilityCheckId(int id) => "compatibility-" + id.ToString(CultureInfo.InvariantCulture);

    private sealed record CompatibilityGroup(ConfiguredConsumableType Type, IReadOnlyList<ConfiguredConsumable> Consumables);
}
