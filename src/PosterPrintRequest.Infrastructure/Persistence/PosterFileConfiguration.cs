using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

internal sealed class PosterFileConfiguration : IEntityTypeConfiguration<PosterFile>
{
    public void Configure(EntityTypeBuilder<PosterFile> builder)
    {
        builder.HasKey(file => file.PosterFileId);

        builder.Property(file => file.OriginalFileName)
            .IsRequired();

        builder.Property(file => file.DetectedFormat)
            .IsRequired();

        builder.Property(file => file.PageCount)
            .IsRequired();

        builder.Property(file => file.Width)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(file => file.Length)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(file => file.StoragePath)
            .IsRequired();
    }
}
