using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PosterPrintRequest.Domain;

namespace PosterPrintRequest.Infrastructure.Persistence;

internal sealed class PrintingInventorySettingConfiguration : IEntityTypeConfiguration<PrintingInventorySetting>
{
    public void Configure(EntityTypeBuilder<PrintingInventorySetting> builder)
    {
        builder.HasKey(setting => setting.PrintingInventorySettingId);
    }
}
