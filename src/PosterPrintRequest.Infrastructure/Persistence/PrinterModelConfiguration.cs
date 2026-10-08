using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

public sealed class PrinterModelConfiguration : IEntityTypeConfiguration<PrinterModel>
{
    public const int NameMaxLength = 128;

    public void Configure(EntityTypeBuilder<PrinterModel> builder)
    {
        builder.HasKey(model => model.PrinterModelId);

        builder.Property(model => model.Name)
            .HasMaxLength(NameMaxLength)
            .IsRequired();

        builder.HasIndex(model => model.Name)
            .IsUnique();

        builder.Property(model => model.Active)
            .HasDefaultValue(true);
    }
}
