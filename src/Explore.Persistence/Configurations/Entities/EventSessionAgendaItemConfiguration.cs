using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventSessionAgendaItemConfiguration : IEntityTypeConfiguration<EventSessionAgendaItem>
{
    public void Configure(EntityTypeBuilder<EventSessionAgendaItem> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(e => e.Title).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.HasOne(e => e.EventSession)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventSessionId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.EventLocation)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventLocationId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.LocationId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
            // ELP-230C contraction: a physical venue reference is only legal when it is mediated by an
            // event-scoped EventLocation. This closes the legacy write path that could attach a raw
            // Location without a per-event disclosure policy.
            t.HasCheckConstraint(
                "ck_event_session_agenda_item_physical_location_requires_event_location",
                "location_id IS NULL OR event_location_id IS NOT NULL"));

        builder.HasIndex(e => new { e.TenantId, e.EventSessionId, e.EventLocationId, e.LocationId });

        builder.HasAnnotation(
            "EventLocationPrivacy:ConsistencyTrigger",
            "event_session_agenda_items:tenant_id,event_session_id,event_location_id,location_id");
    }
}
