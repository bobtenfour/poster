using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

public sealed class PosterPrintRequestDbContext : DbContext
{
    public PosterPrintRequestDbContext(DbContextOptions<PosterPrintRequestDbContext> options)
        : base(options)
    {
    }

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Reason> Reasons => Set<Reason>();

    public DbSet<PosterRequest> PosterRequests => Set<PosterRequest>();

    public DbSet<PosterFile> PosterFiles => Set<PosterFile>();

    public DbSet<ApprovalSheet> ApprovalSheets => Set<ApprovalSheet>();

    public DbSet<PosterProcessing> PosterProcessings => Set<PosterProcessing>();

    public DbSet<PosterRetentionAudit> PosterRetentionAudits => Set<PosterRetentionAudit>();

    public DbSet<PosterReplacement> PosterReplacements => Set<PosterReplacement>();

    public DbSet<ConsumableType> ConsumableTypes => Set<ConsumableType>();

    public DbSet<PrintingConsumable> PrintingConsumables => Set<PrintingConsumable>();

    public DbSet<PrintingStockMovement> PrintingStockMovements => Set<PrintingStockMovement>();

    public DbSet<PrintingStockEntry> PrintingStockEntries => Set<PrintingStockEntry>();

    public DbSet<PrinterModel> PrinterModels => Set<PrinterModel>();

    public DbSet<PrinterModelConsumable> PrinterModelConsumables => Set<PrinterModelConsumable>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PosterPrintRequestDbContext).Assembly);
    }
}
