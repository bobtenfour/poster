using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

internal sealed class PosterRequestConfiguration : IEntityTypeConfiguration<PosterRequest>
{
    public const int PosterIdMaxLength = 64;

    public void Configure(EntityTypeBuilder<PosterRequest> builder)
    {
        builder.HasKey(request => request.PosterRequestId);

        builder.Property(request => request.PosterId)
            .HasMaxLength(PosterIdMaxLength)
            .IsRequired();

        builder.HasIndex(request => request.PosterId)
            .IsUnique();

        builder.Property(request => request.Name)
            .IsRequired();

        builder.Property(request => request.Mentor)
            .IsRequired(false);

        builder.Property(request => request.Room)
            .IsRequired();

        builder.Property(request => request.Phone)
            .IsRequired();

        builder.Property(request => request.Email)
            .IsRequired();

        builder.Property(request => request.SubmittedByUserName)
            .HasMaxLength(256);

        builder.Property(request => request.LaminationRequested)
            .IsRequired();

        builder.Property(request => request.ApprovalSheetUploaded)
            .IsRequired();

        builder.Property(request => request.DateIn)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.HasOne(request => request.Department)
            .WithMany(department => department.PosterRequests)
            .HasForeignKey(request => request.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(request => request.Reason)
            .WithMany(reason => reason.PosterRequests)
            .HasForeignKey(request => request.ReasonId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne(request => request.PosterFile)
            .WithOne(file => file.PosterRequest)
            .HasForeignKey<PosterFile>(file => file.PosterRequestId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Navigation(request => request.PosterFile)
            .IsRequired();

        builder.HasOne(request => request.ApprovalSheet)
            .WithOne(sheet => sheet.PosterRequest)
            .HasForeignKey<ApprovalSheet>(sheet => sheet.PosterRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(request => request.ApprovalSheet)
            .IsRequired(false);

        builder.HasOne(request => request.PosterProcessing)
            .WithOne(processing => processing.PosterRequest)
            .HasForeignKey<PosterProcessing>(processing => processing.PosterRequestId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Navigation(request => request.PosterProcessing)
            .IsRequired();
    }
}
