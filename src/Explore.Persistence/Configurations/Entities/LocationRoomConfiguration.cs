using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class LocationRoomConfiguration : IEntityTypeConfiguration<LocationRoom>
{
    public void Configure(EntityTypeBuilder<LocationRoom> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasAlternateKey(e => new { e.TenantId, e.Id });
        builder.HasAlternateKey(e => new { e.TenantId, e.LocationId, e.Id });

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Slug).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.LocationId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.LocationId, e.Name })
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.LocationId, e.SortOrder });

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_location_room_non_negative_capacity",
            "capacity IS NULL OR capacity >= 0"));

        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
    }
}
