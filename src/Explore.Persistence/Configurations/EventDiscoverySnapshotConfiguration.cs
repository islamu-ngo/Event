using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations;

public sealed class EventDiscoverySnapshotConfiguration : IEntityTypeConfiguration<EventDiscoverySnapshot>
{
    public void Configure(EntityTypeBuilder<EventDiscoverySnapshot> builder)
    {
        builder.ToTable("event_discovery_snapshots", table =>
        {
            table.HasCheckConstraint("ck_discovery_snapshot_hash", "length(criteria_hash) = 64");
            table.HasCheckConstraint("ck_discovery_snapshot_epochs", "identity_epoch >= 0 AND disclosure_epoch >= 0");
            table.HasCheckConstraint("ck_discovery_snapshot_count", "item_count BETWEEN 0 AND 1000");
            table.HasCheckConstraint("ck_discovery_snapshot_expiry", "expires_at_utc > created_at_utc");
        });
        builder.HasKey(snapshot => snapshot.Id);
        builder.Property(snapshot => snapshot.Id).ValueGeneratedNever();
        builder.HasAlternateKey(snapshot => new { snapshot.TenantId, snapshot.Id });
        builder.Property(snapshot => snapshot.CriteriaHash).HasMaxLength(64).IsRequired();
        builder.Property(snapshot => snapshot.CreatedAtUtc)
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(snapshot => snapshot.ExpiresAtUtc)
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.HasIndex(snapshot => new
        {
            snapshot.TenantId,
            snapshot.CriteriaHash,
            snapshot.IdentityEpoch,
            snapshot.DisclosureEpoch,
            snapshot.ExpiresAtUtc
        });
        builder.HasIndex(snapshot => new { snapshot.TenantId, snapshot.ExpiresAtUtc, snapshot.Id });
        builder.HasOne<EventDiscoverySnapshotReservation>().WithMany().HasForeignKey(snapshot => snapshot.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(snapshot => snapshot.Items).WithOne()
            .HasForeignKey(item => new { item.TenantId, item.SnapshotId })
            .HasPrincipalKey(snapshot => new { snapshot.TenantId, snapshot.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(snapshot => snapshot.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class EventDiscoverySnapshotItemConfiguration : IEntityTypeConfiguration<EventDiscoverySnapshotItem>
{
    public void Configure(EntityTypeBuilder<EventDiscoverySnapshotItem> builder)
    {
        builder.ToTable("event_discovery_snapshot_items", table =>
        {
            table.HasCheckConstraint("ck_discovery_snapshot_item_ordinal", "ordinal BETWEEN 0 AND 999");
            table.HasCheckConstraint("ck_discovery_snapshot_item_source", "source_kind IN (1, 2)");
            table.HasCheckConstraint("ck_discovery_snapshot_item_canonical", "canonical_kind IN (1, 2, 3)");
        });
        builder.HasKey(item => new { item.TenantId, item.SnapshotId, item.Ordinal });
        builder.Property(item => item.SourceKind).HasConversion<int>();
        builder.Property(item => item.CanonicalKind).HasConversion<int>();
        builder.HasIndex(item => new { item.TenantId, item.SnapshotId, item.CanonicalKind, item.CanonicalId }).IsUnique();
    }
}
