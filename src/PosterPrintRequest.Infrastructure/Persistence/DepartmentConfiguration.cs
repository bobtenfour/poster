using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public const int NameMaxLength = 128;

    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.HasKey(department => department.DepartmentId);

        builder.Property(department => department.Name)
            .HasMaxLength(NameMaxLength)
            .IsRequired();

        builder.HasIndex(department => department.Name)
            .IsUnique();

        builder.Property(department => department.Active)
            .HasDefaultValue(true);
    }
}
