using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

public sealed class ReasonConfiguration : IEntityTypeConfiguration<Reason>
{
    public const int NameMaxLength = 128;

    public void Configure(EntityTypeBuilder<Reason> builder)
    {
        builder.HasKey(reason => reason.ReasonId);

        builder.Property(reason => reason.Name)
            .HasMaxLength(NameMaxLength)
            .IsRequired();

        builder.HasIndex(reason => reason.Name)
            .IsUnique();

        builder.Property(reason => reason.RequiresMentor)
            .IsRequired();

        builder.Property(reason => reason.RequiresApprovalSheet)
            .IsRequired();

        builder.Property(reason => reason.Active)
            .HasDefaultValue(true);
    }
}
