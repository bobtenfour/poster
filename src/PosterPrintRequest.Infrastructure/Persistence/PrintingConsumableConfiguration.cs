using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

public sealed class PrintingConsumableConfiguration : IEntityTypeConfiguration<PrintingConsumable>
{
    public const int CategoryMaxLength = 32;

    public const int NameMaxLength = 128;

    public const int CodeMaxLength = 32;

    public const int CapacityMaxLength = 80;

    public const int StatusMaxLength = 32;

    public void Configure(EntityTypeBuilder<PrintingConsumable> builder)
    {
        builder.HasKey(item => item.PrintingConsumableId);

        builder.Property(item => item.Category)
            .HasMaxLength(CategoryMaxLength)
            .IsRequired();

        builder.Property(item => item.Name)
            .HasMaxLength(NameMaxLength)
            .IsRequired();

        builder.Property(item => item.Code)
            .HasMaxLength(CodeMaxLength);

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasFilter("[Code] IS NOT NULL");

        builder.Property(item => item.Capacity)
            .HasMaxLength(CapacityMaxLength);

        builder.Property(item => item.Status)
            .HasMaxLength(StatusMaxLength)
            .IsRequired();

        builder.Property(item => item.ExpirationDate)
            .HasColumnType("date");
    }
}
