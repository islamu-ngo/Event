using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventLocationConfiguration : IEntityTypeConfiguration<EventLocation>
{
    public void Configure(EntityTypeBuilder<EventLocation> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_event_locations_physical_or_tba",
                "(location_id IS NOT NULL AND is_to_be_announced = false) OR " +
                "(location_id IS NULL AND is_to_be_announced = true)");
            table.HasCheckConstraint(
                "ck_event_locations_tba_suppresses_fields",
                "is_to_be_announced = false OR " +
                "(show_venue_name = false AND show_city = false AND show_country = false AND " +
                "show_room_name = false AND show_street_address = false AND show_postcode = false AND " +
                "show_coordinates = false)");
            table.HasCheckConstraint("ck_event_locations_policy_version", "policy_version > 0");
            table.HasCheckConstraint(
                "ck_event_locations_uuid_v7",
                "substring(id::text, 15, 1) = '7' AND substring(id::text, 20, 1) IN ('8', '9', 'a', 'b')");
        });

        builder.Property(item => item.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasAlternateKey(item => new { item.TenantId, item.Id });
        builder.HasAlternateKey(item => new { item.TenantId, item.EventId, item.Id });
        builder.Property(item => item.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(item => item.Tenant)
            .WithMany()
            .HasForeignKey(item => item.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Event)
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.EventId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Location)
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.LocationId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.FullDetailsAudience)
            .WithMany()
            .HasForeignKey(item => item.FullDetailsAudienceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(item => new { item.TenantId, item.EventId, item.LocationId })
            .IsUnique()
            .HasFilter("is_deleted = false AND is_to_be_announced = false AND location_id IS NOT NULL");
        builder.HasIndex(item => new { item.TenantId, item.EventId })
            .IsUnique()
            .HasFilter("is_deleted = false AND is_to_be_announced = true");
        builder.HasIndex(item => new { item.TenantId, item.EventId, item.IsDeleted });
    }
}
