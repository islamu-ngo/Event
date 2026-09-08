using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class TenantFooterLinkConfiguration : IEntityTypeConfiguration<TenantFooterLink>
{
    public void Configure(EntityTypeBuilder<TenantFooterLink> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.Label)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Url)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(e => e.OpenInNewTab)
            .HasDefaultValue(false);

        builder.Property(e => e.Order)
            .HasDefaultValue(0);

        builder.Property(e => e.IsActive)
            .HasDefaultValue(true);

        // FK relationship is configured from the group side; declare navigation here for clarity
        builder.HasOne(e => e.Group)
            .WithMany(g => g.Links)
            .HasForeignKey(e => e.FooterLinkGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.FooterLinkGroupId, e.Order });
    }
}
