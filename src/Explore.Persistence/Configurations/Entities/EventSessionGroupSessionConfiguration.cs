using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventSessionGroupSessionConfiguration : IEntityTypeConfiguration<EventSessionGroupSession>
{
    public void Configure(EntityTypeBuilder<EventSessionGroupSession> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.HasOne(e => e.EventSessionGroup)
            .WithMany(e => e.Sessions)
            .HasForeignKey(e => new { e.TenantId, e.EventId, e.EventSessionGroupId })
            .HasPrincipalKey(e => new { e.TenantId, e.EventId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.EventSession)
            .WithMany(e => e.SessionGroups)
            .HasForeignKey(e => new { e.TenantId, e.EventId, e.EventSessionId })
            .HasPrincipalKey(e => new { e.TenantId, e.EventId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Event)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.EventSessionGroupId, e.EventSessionId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.EventSessionId, e.IsPrimary })
            .IsUnique()
            .HasFilter("is_primary = true AND is_deleted = false");

        builder.HasIndex(e => new { e.TenantId, e.EventSessionGroupId, e.SortOrder });

        builder.Property(e => e.IsDeleted).HasDefaultValue(false);
    }
}
