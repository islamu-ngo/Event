using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventContactShareExportItemConfiguration : IEntityTypeConfiguration<EventContactShareExportItem>
{
    public void Configure(EntityTypeBuilder<EventContactShareExportItem> builder)
    {
        builder.HasKey(e => new { e.ExportId, e.ConsentId });

        builder.Property(e => e.ExportedFieldSnapshot).IsRequired().HasMaxLength(4000);

        builder.HasOne(e => e.Export).WithMany(x => x.Items).HasForeignKey(e => e.ExportId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Consent).WithMany().HasForeignKey(e => e.ConsentId).OnDelete(DeleteBehavior.Restrict);
    }
}
