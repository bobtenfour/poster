using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

/// <summary>Consumables Inventory Contract sections 7.1 and 7.2.</summary>
public sealed class PrintingStockMovementConfiguration : IEntityTypeConfiguration<PrintingStockMovement>
{
    public const int MovementTypeMaxLength = 16;

    public const int ReasonMaxLength = 500;

    public const int AccountNameMaxLength = 256;

    public void Configure(EntityTypeBuilder<PrintingStockMovement> builder)
    {
        builder.ToTable("PrintingStockMovements", table =>
        {
            table.HasCheckConstraint(
                "CK_PrintingStockMovements_MovementType",
                "[MovementType] IN (N'Received', N'Used', N'Adjustment')");
            table.HasCheckConstraint(
                "CK_PrintingStockMovements_QuantityChange",
                "[QuantityChange] <> 0 AND ([MovementType] <> N'Received' OR [QuantityChange] > 0) AND ([MovementType] <> N'Used' OR [QuantityChange] < 0)");
            table.HasCheckConstraint("CK_PrintingStockMovements_LotQuantityAfter", "[LotQuantityAfter] >= 0");
            table.HasCheckConstraint("CK_PrintingStockMovements_OutOfOrder", "[OutOfOrder] = 0 OR [MovementType] = N'Used'");
            table.HasCheckConstraint(
                "CK_PrintingStockMovements_Reason",
                "([MovementType] = N'Adjustment' AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) OR ([MovementType] <> N'Adjustment' AND [Reason] IS NULL)");
        });

        builder.HasKey(movement => movement.PrintingStockMovementId);

        builder.Property(movement => movement.ExpirationDate)
            .HasColumnType("date");

        builder.Property(movement => movement.MovementType)
            .HasMaxLength(MovementTypeMaxLength)
            .IsRequired();

        builder.Property(movement => movement.OutOfOrder)
            .HasDefaultValue(false);

        builder.Property(movement => movement.Reason)
            .HasMaxLength(ReasonMaxLength);

        builder.Property(movement => movement.OccurredAt)
            .HasColumnType("datetime2");

        builder.Property(movement => movement.PerformedByAccountName)
            .HasMaxLength(AccountNameMaxLength);

        builder.HasOne(movement => movement.PrintingConsumable)
            .WithMany(item => item.StockMovements)
            .HasForeignKey(movement => movement.PrintingConsumableId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(movement => new { movement.PrintingConsumableId, movement.OccurredAt });

        builder.HasIndex(movement => movement.OperationId);
    }
}
