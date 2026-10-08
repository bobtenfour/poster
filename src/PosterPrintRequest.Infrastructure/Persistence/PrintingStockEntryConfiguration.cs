using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

public sealed class PrintingStockEntryConfiguration : IEntityTypeConfiguration<PrintingStockEntry>
{
    public void Configure(EntityTypeBuilder<PrintingStockEntry> builder)
    {
        builder.HasKey(entry => entry.PrintingStockEntryId);

        builder.Property(entry => entry.ExpirationDate)
            .HasColumnType("date");

        builder.HasOne(entry => entry.PrintingConsumable)
            .WithMany(item => item.StockEntries)
            .HasForeignKey(entry => entry.PrintingConsumableId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
