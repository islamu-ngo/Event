using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations;

public sealed class EventDiscoveryIdentityConfiguration : IEntityTypeConfiguration<EventDiscoveryIdentity>
{
    public void Configure(EntityTypeBuilder<EventDiscoveryIdentity> builder)
    {
        builder.ToTable("event_discovery_identities", table =>
        {
            table.HasCheckConstraint("ck_discovery_identity_source_kind", "source_kind IN (1, 2)");
            table.HasCheckConstraint("ck_discovery_identity_source_key", "length(source_key) BETWEEN 1 AND 1024");
            table.HasCheckConstraint("ck_discovery_identity_source_hash", "length(source_key_hash) = 64");
        });
        builder.HasKey(identity => identity.Id);
        builder.Property(identity => identity.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasAlternateKey(identity => new { identity.TenantId, identity.Id });
        builder.Property(identity => identity.SourceKind).HasConversion<int>();
        builder.Property(identity => identity.SourceKey).HasMaxLength(1024).IsRequired();
        builder.Property(identity => identity.SourceKeyHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(identity => new { identity.TenantId, identity.SourceKind, identity.SourceKeyHash })
            .IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(identity => identity.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
