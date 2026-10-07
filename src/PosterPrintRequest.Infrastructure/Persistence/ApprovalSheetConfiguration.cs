using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

internal sealed class ApprovalSheetConfiguration : IEntityTypeConfiguration<ApprovalSheet>
{
    public void Configure(EntityTypeBuilder<ApprovalSheet> builder)
    {
        builder.HasKey(sheet => sheet.ApprovalSheetId);

        builder.Property(sheet => sheet.FileName)
            .IsRequired();

        builder.Property(sheet => sheet.StoragePath)
            .IsRequired();
    }
}
