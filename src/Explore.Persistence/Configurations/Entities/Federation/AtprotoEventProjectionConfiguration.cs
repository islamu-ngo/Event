using Explore.Domain.Federation;
using Explore.Domain.Services.Discovery;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities.Federation;

public sealed class AtprotoEventProjectionConfiguration : IEntityTypeConfiguration<AtprotoEventProjection>
{
    public void Configure(EntityTypeBuilder<AtprotoEventProjection> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_atproto_event_projections_source_version", "source_version >= 0");
            table.HasCheckConstraint(
                "ck_atproto_event_projections_time_order",
                "ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at");
        });
        builder.HasKey(value => value.AtprotoRecordId);
        builder.Property(value => value.AtprotoRecordId).UsePropertyAccessMode(PropertyAccessMode.Property);
        builder.Property(value => value.Name).HasMaxLength(EventDiscoveryRank.ProjectionNameMaximumLength).IsRequired()
            .UsePropertyAccessMode(PropertyAccessMode.Property);
        builder.Property(value => value.DiscoveryTitleSortKey)
            .HasMaxLength(EventDiscoveryRank.ProjectionTitleKeyMaximumLength).IsRequired().IsUnicode(false)
            .UsePortableOrdinalAscii();
        builder.Property(value => value.DiscoverySourceSortKey)
            .HasMaxLength(EventDiscoveryRank.SourceKeyLength).IsRequired().IsUnicode(false)
            .UsePortableOrdinalAscii();
        builder.Property(value => value.Description).HasMaxLength(4000);
        builder.Property(value => value.Mode).HasMaxLength(80);
        builder.Property(value => value.Status).HasMaxLength(80);
        builder.Property(value => value.LocationSummary).HasMaxLength(500);
        builder.Property(value => value.SourceUrl).HasMaxLength(2048);
        builder.Property(value => value.MaterializedAt).IsRequired();
        builder.HasOne(value => value.AtprotoRecord)
            .WithOne()
            .HasForeignKey<AtprotoEventProjection>(value => value.AtprotoRecordId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(value => new { value.StartsAt, value.DiscoverySourceSortKey });
        builder.HasIndex(value => new { value.CreatedAt, value.DiscoverySourceSortKey });
        builder.HasIndex(value => new { value.DiscoveryTitleSortKey, value.DiscoverySourceSortKey });
        builder.HasIndex(value => value.DiscoverySourceSortKey);
    }
}
