using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations;

public sealed class EventDiscoveryRevisionConfiguration : IEntityTypeConfiguration<EventDiscoveryRevision>
{
    public void Configure(EntityTypeBuilder<EventDiscoveryRevision> builder)
    {
        builder.ToTable("event_discovery_revisions", table =>
        {
            table.HasCheckConstraint("ck_discovery_revision_epochs", "identity_epoch >= 0 AND disclosure_epoch >= 0");
        });
        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasIndex(revision => revision.TenantId).IsUnique();
        builder.Property(revision => revision.IdentityEpoch).IsConcurrencyToken();
        builder.Property(revision => revision.DisclosureEpoch).IsConcurrencyToken();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(revision => revision.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
