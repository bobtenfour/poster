using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

/// <summary>Consumables Inventory Contract sections 4.1 and 4.1a.</summary>
public sealed class ConsumableTypeConfiguration : IEntityTypeConfiguration<ConsumableType>
{
    public const int NameMaxLength = 64;

    public const int StockModeMaxLength = 16;

    public const string NameKeyIndex = "IX_ConsumableTypes_NameKey";

    public void Configure(EntityTypeBuilder<ConsumableType> builder)
    {
        builder.ToTable("ConsumableTypes", table =>
        {
            table.HasCheckConstraint(
                "CK_ConsumableTypes_Name",
                "DATALENGTH([Name]) > 0 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name])))");
            table.HasCheckConstraint(
                "CK_ConsumableTypes_StockMode",
                "[StockMode] IN (N'Undated', N'DateRequired', N'DateOptional')");
            table.HasCheckConstraint(
                "CK_ConsumableTypes_Acknowledge",
                "[AcknowledgeEarliestExpiration] = 0 OR [StockMode] <> N'Undated'");
            table.HasCheckConstraint("CK_ConsumableTypes_DisplayOrder", "[DisplayOrder] >= 0");
        });

        builder.HasKey(type => type.ConsumableTypeId);

        builder.Property(type => type.Name)
            .HasMaxLength(NameMaxLength)
            .IsRequired();

        builder.Property(type => type.NameKey)
            .HasMaxLength(NameMaxLength)
            .HasComputedColumnSql("UPPER([Name]) COLLATE Latin1_General_100_BIN2", stored: true)
            .ValueGeneratedOnAddOrUpdate();

        builder.HasIndex(type => type.NameKey)
            .IsUnique()
            .HasDatabaseName(NameKeyIndex);

        builder.Property(type => type.StockMode)
            .HasMaxLength(StockModeMaxLength)
            .IsRequired();

        builder.Property(type => type.AcknowledgeEarliestExpiration)
            .HasDefaultValue(false);

        builder.Property(type => type.UsesPrinterCompatibility)
            .HasDefaultValue(true);

        builder.Property(type => type.Active)
            .HasDefaultValue(true);
    }
}
