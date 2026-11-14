using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

internal sealed class PosterRetentionAuditConfiguration : IEntityTypeConfiguration<PosterRetentionAudit>
{
    public const int ActionMaxLength = 32;

    public const int DirectoryMaxLength = 512;

    public void Configure(EntityTypeBuilder<PosterRetentionAudit> builder)
    {
        builder.HasKey(audit => audit.PosterRetentionAuditId);

        builder.Property(audit => audit.PosterId)
            .HasMaxLength(PosterRequestConfiguration.PosterIdMaxLength)
            .IsRequired();

        builder.Property(audit => audit.Action)
            .HasMaxLength(ActionMaxLength)
            .IsRequired();

        builder.Property(audit => audit.OccurredAt)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.Property(audit => audit.OriginalDirectory)
            .HasMaxLength(DirectoryMaxLength)
            .IsRequired();

        builder.HasOne<PosterRequest>()
            .WithMany()
            .HasForeignKey(audit => audit.PosterRequestId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasIndex(audit => audit.PosterRequestId);
    }
}
