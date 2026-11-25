using PosterPrintRequest.Tests.Hosting;
using PosterPrintRequest.Web;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Tests.Workflow;

/// <summary>The four types created by the PHASE 16 migration (Consumables Inventory Contract section 5.3).</summary>
internal static class InitialTypes
{
    public const string Cartridge = "Cartridges";

    public const string Paper = "Paper";

    public const string Laminating = "Laminating materials";

    public const string PrinterHeads = "Printer heads";
}

/// <summary>Test shorthands: types by name, and stock operations by the signed-in test technician.</summary>
internal static class InventoryTestSupport
{
    public static readonly InstitutionalActor Technician = new(InstitutionalTestIdentities.TechnicianOid, null, "tech@example.edu");

    /// <summary>Removes types added by tests and restores the four initial types as the migration created them.</summary>
    public static async Task ResetTypesAsync(PosterPrintRequest.Infrastructure.Persistence.PosterPrintRequestDbContext context)
    {
        string[] initial = [InitialTypes.Cartridge, InitialTypes.Paper, InitialTypes.Laminating, InitialTypes.PrinterHeads];
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(context.ConsumableTypes);
        foreach (var type in context.ConsumableTypes.Local.ToList())
        {
            if (!initial.Contains(type.Name))
            {
                context.ConsumableTypes.Remove(type);
                continue;
            }

            type.Active = true;
            type.UsesPrinterCompatibility = true;
            (type.StockMode, type.AcknowledgeEarliestExpiration, type.DisplayOrder) = type.Name switch
            {
                InitialTypes.Cartridge => (PosterPrintRequest.Domain.ConsumableStockModes.DateRequired, true, 1),
                InitialTypes.Paper => (PosterPrintRequest.Domain.ConsumableStockModes.Undated, false, 2),
                InitialTypes.Laminating => (PosterPrintRequest.Domain.ConsumableStockModes.DateOptional, false, 3),
                _ => (PosterPrintRequest.Domain.ConsumableStockModes.DateRequired, true, 4)
            };
        }

        await context.SaveChangesAsync();
    }

    public static IReadOnlyList<ConsumableRow> Rows(this PrintingInventoryView view, string typeName) =>
        view.Types.SingleOrDefault(type => type.Name == typeName)?.Rows ?? [];

    public static int Count(this SupplyCategorySummary summary, string typeName) =>
        summary.ByType.SingleOrDefault(type => type.Name == typeName)?.Count ?? 0;

    public static async Task<int> TypeIdAsync(this IOperatorConfiguration configuration, string typeName) =>
        (await configuration.LoadAsync(CancellationToken.None)).ConsumableTypes.Single(type => type.Name == typeName).Id;

    public static async Task<ConfigurationOutcome> AddConsumableAsync(
        this IOperatorConfiguration configuration,
        string typeName,
        string? name,
        string? code,
        string? capacity,
        CancellationToken cancellationToken) =>
        await configuration.AddConsumableAsync(await configuration.TypeIdAsync(typeName), name, code, capacity, cancellationToken);

    public static Task<InventoryOutcome> AddStockAsync(this IPrintingInventory inventory, int id, int quantity, DateOnly? date, CancellationToken cancellationToken) =>
        inventory.AddStockAsync(id, quantity, date, Technician, cancellationToken);

    public static Task<InventoryOutcome> RemoveStockAsync(
        this IPrintingInventory inventory,
        int id,
        int quantity,
        bool acknowledged,
        DateOnly? date,
        CancellationToken cancellationToken) =>
        inventory.RemoveStockAsync(id, quantity, acknowledged, date, Technician, cancellationToken);
}
