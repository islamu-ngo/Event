using Explore.Domain.Federation;
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
        builder.Property(value => value.Name).HasMaxLength(240).IsRequired();
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
        builder.HasIndex(value => new { value.StartsAt, value.AtprotoRecordId });
        builder.HasIndex(value => new { value.CreatedAt, value.AtprotoRecordId });
        builder.HasIndex(value => new { value.Name, value.AtprotoRecordId });
    }
}
