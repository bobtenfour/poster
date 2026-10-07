using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Identity;

namespace PosterPrintRequest.Infrastructure.Persistence;

public sealed class PosterPrintRequestDbContext : IdentityDbContext<ApplicationUser>
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PosterPrintRequestDbContext).Assembly);
    }
}
