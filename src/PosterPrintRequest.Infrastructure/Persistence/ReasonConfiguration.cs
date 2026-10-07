using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

internal sealed class ReasonConfiguration : IEntityTypeConfiguration<Reason>
{
    public void Configure(EntityTypeBuilder<Reason> builder)
    {
        builder.HasKey(reason => reason.ReasonId);

        builder.Property(reason => reason.Name)
            .IsRequired();

        builder.Property(reason => reason.RequiresMentor)
            .IsRequired();

        builder.Property(reason => reason.RequiresApprovalSheet)
            .IsRequired();
    }
}
