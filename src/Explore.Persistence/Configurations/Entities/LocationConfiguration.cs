using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Schema;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.Property(e => e.FullName).HasMaxLength(LocationTextNormalization.MaximumSourceLength).IsRequired();
        builder.Property(e => e.DisplaySortKey)
            .HasMaxLength(LocationTextNormalization.MaximumKeyLength)
            .IsUnicode()
            .IsRequired()
            .UseLocationUnicodeCollation();
        builder.Property(e => e.DisplaySortKeyVersion)
            .IsRequired();
        builder.Property(e => e.Country).HasMaxLength(500).IsRequired();
        builder.Property(e => e.City).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Timezone).HasMaxLength(500);
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
        builder.Property(e => e.LocationKindId)
            .HasDefaultValue((int)LocationKindEnum.Unclassified)
            .IsRequired();
        builder.Property(e => e.LocationPrivacyStateId)
            .HasDefaultValue((int)LocationPrivacyStateEnum.NotProvided)
            .IsRequired();
        builder.Property(e => e.AddressSourceId)
            .HasDefaultValue((int)LocationAddressSourceEnum.UnknownLegacy)
            .IsRequired();
        builder.Property(e => e.AddressVisibilityId)
            .HasDefaultValue((int)LocationAddressVisibilityEnum.Quarantined)
            .IsRequired();
        builder.Ignore(e => e.AddressSource);
        builder.Ignore(e => e.AddressVisibility);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Pii)
            .WithOne(e => e.Location)
            .HasForeignKey<LocationPii>(e => e.LocationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.Pii).AutoInclude();

        builder.HasOne(e => e.LocationKind)
            .WithMany()
            .HasForeignKey(e => e.LocationKindId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.LocationPrivacyState)
            .WithMany()
            .HasForeignKey(e => e.LocationPrivacyStateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.AddressSourceLookup)
            .WithMany()
            .HasForeignKey(e => e.AddressSourceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.AddressVisibilityLookup)
            .WithMany()
            .HasForeignKey(e => e.AddressVisibilityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.AddressOrganizationTenant)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.AddressOrganizationId })
            .HasPrincipalKey(e => new { e.TenantId, e.OrganizationId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.OwnerUser)
            .WithMany()
            .HasForeignKey(e => e.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_locations_owner_private_home",
                "owner_user_id IS NULL OR location_kind_id = 5");
            table.HasCheckConstraint(
                "ck_locations_erasure_state",
                "(location_privacy_state_id = 3 AND owner_user_id IS NULL AND pii_erased_at_utc IS NOT NULL AND pii_erasure_reason IS NOT NULL) OR " +
                "(location_privacy_state_id <> 3 AND pii_erased_at_utc IS NULL AND pii_erasure_reason IS NULL)");
            table.HasCheckConstraint(
                "ck_locations_address_visibility_scope",
                "(address_visibility_id = 1 AND address_organization_id IS NULL) OR " +
                "(address_visibility_id = 2 AND created_by IS NOT NULL AND address_organization_id IS NULL) OR " +
                "(address_visibility_id = 3 AND created_by IS NOT NULL AND address_organization_id IS NOT NULL) OR " +
                "address_visibility_id = 4");
            table.HasCheckConstraint(
                "ck_locations_private_home_address_visibility",
                "location_kind_id <> 5 OR address_visibility_id <> 4");
            table.HasCheckConstraint(
                "ck_locations_erased_address_quarantined",
                "location_privacy_state_id <> 3 OR (address_visibility_id = 1 AND address_organization_id IS NULL)");
            table.HasCheckConstraint(
                "ck_locations_display_sort_key_version",
                $"display_sort_key_version = {LocationTextNormalization.CurrentRevision} AND display_sort_key <> ''");
        });

        // ===== Performance Indexes =====

        // Location lookup by city (most common filter)
        builder.HasIndex(e => new { e.TenantId, e.City });

        // Location lookup by country
        builder.HasIndex(e => new { e.TenantId, e.Country });

        builder.HasIndex(e => new { e.TenantId, e.AddressVisibilityId, e.CreatedBy });

        builder.HasIndex(e => new { e.TenantId, e.AddressVisibilityId, e.AddressOrganizationId });

        // NOTE: Business entity seed data moved to DatabaseSeeder for conditional (Development-only) seeding.
        // See Explore.Persistence/Seed/DatabaseSeeder.cs and SeedData.cs
    }
}
