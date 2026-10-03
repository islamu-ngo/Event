using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations;

public sealed class EventDiscoveryAliasConfiguration : IEntityTypeConfiguration<EventDiscoveryAlias>
{
    public void Configure(EntityTypeBuilder<EventDiscoveryAlias> builder)
    {
        builder.ToTable("event_discovery_aliases", table =>
        {
            table.HasCheckConstraint("ck_discovery_alias_not_self", "member_identity_id <> primary_identity_id");
            table.HasCheckConstraint("ck_discovery_alias_revision", "relationship_revision > 0");
            table.HasCheckConstraint("ck_discovery_alias_reason", "length(reason_code) BETWEEN 1 AND 80");
        });
        builder.HasKey(alias => alias.Id);
        builder.Property(alias => alias.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(alias => alias.ReasonCode).HasMaxLength(80).IsRequired();
        builder.Property(alias => alias.RelationshipRevision).IsConcurrencyToken();
        builder.HasOne(alias => alias.Member).WithOne(identity => identity.Alias)
            .HasForeignKey<EventDiscoveryAlias>(alias => new { alias.TenantId, alias.MemberIdentityId })
            .HasPrincipalKey<EventDiscoveryIdentity>(identity => new { identity.TenantId, identity.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(alias => alias.Primary).WithMany()
            .HasForeignKey(alias => new { alias.TenantId, alias.PrimaryIdentityId })
            .HasPrincipalKey(identity => new { identity.TenantId, identity.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(alias => new { alias.TenantId, alias.PrimaryIdentityId });
    }
}
