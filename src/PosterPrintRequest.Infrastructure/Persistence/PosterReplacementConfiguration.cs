using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

internal sealed class PosterReplacementConfiguration : IEntityTypeConfiguration<PosterReplacement>
{
    public const int NoteMaxLength = 500;

    public const int ArchivedFilePathMaxLength = 400;

    public void Configure(EntityTypeBuilder<PosterReplacement> builder)
    {
        builder.HasKey(replacement => replacement.PosterReplacementId);

        builder.Property(replacement => replacement.ReplacedAt)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.Property(replacement => replacement.Note)
            .HasMaxLength(NoteMaxLength)
            .IsRequired();

        builder.Property(replacement => replacement.ArchivedFilePath)
            .HasMaxLength(ArchivedFilePathMaxLength)
            .IsRequired();

        builder.Property(replacement => replacement.ReplacedByObjectId)
            .IsRequired();

        builder.HasIndex(replacement => replacement.PosterRequestId);
    }
}
