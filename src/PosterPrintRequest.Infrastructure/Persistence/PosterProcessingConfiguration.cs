using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

internal sealed class PosterProcessingConfiguration : IEntityTypeConfiguration<PosterProcessing>
{
    public void Configure(EntityTypeBuilder<PosterProcessing> builder)
    {
        builder.HasKey(processing => processing.PosterProcessingId);

        builder.Property(processing => processing.ProcessedByAccountName)
            .HasMaxLength(PosterRequestConfiguration.AccountNameMaxLength);

        builder.Property(processing => processing.ProcessedBySecurityIdentifier)
            .HasMaxLength(PosterRequestConfiguration.SecurityIdentifierMaxLength);

        builder.Property(processing => processing.ProcessedByObjectId);

        builder.Property(processing => processing.Received)
            .HasColumnType("date");

        builder.Property(processing => processing.Printed)
            .IsRequired();

        builder.Property(processing => processing.Laminated)
            .IsRequired();

        builder.Property(processing => processing.Notified)
            .IsRequired();

        builder.Property(processing => processing.DateOut)
            .HasColumnType("date");
    }
}
