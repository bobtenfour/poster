using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

public sealed class PrinterModelConsumableConfiguration : IEntityTypeConfiguration<PrinterModelConsumable>
{
    public void Configure(EntityTypeBuilder<PrinterModelConsumable> builder)
    {
        builder.HasKey(link => link.PrinterModelConsumableId);

        builder.HasIndex(link => new { link.PrinterModelId, link.PrintingConsumableId })
            .IsUnique();

        builder.Property(link => link.Active)
            .HasDefaultValue(true);

        builder.HasOne(link => link.PrinterModel)
            .WithMany(model => model.CompatibleConsumables)
            .HasForeignKey(link => link.PrinterModelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(link => link.PrintingConsumable)
            .WithMany(item => item.PrinterCompatibilities)
            .HasForeignKey(link => link.PrintingConsumableId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
