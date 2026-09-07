using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class TagTypeTagsConfiguration : IEntityTypeConfiguration<TagTypeTags>
{
    public void Configure(EntityTypeBuilder<TagTypeTags> builder)
    {
        builder.HasIndex(e => new { e.TenantId, e.TagId, e.TagTypeId })
            .IsUnique();

        builder.HasOne(e => e.Tag)
            .WithMany()
            .HasForeignKey(e => e.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.TagType)
            .WithMany()
            .HasForeignKey(e => e.TagTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
