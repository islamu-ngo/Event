using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventAgendaItemConfiguration : IEntityTypeConfiguration<EventAgendaItem>
{
    public void Configure(EntityTypeBuilder<EventAgendaItem> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.Title).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2000);

        builder.HasOne(e => e.Event)
            .WithMany(e => e.AgendaItems)
            .HasForeignKey(e => new { e.TenantId, e.EventId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.EventLocation)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventId, e.EventLocationId })
            .HasPrincipalKey(e => new { e.TenantId, e.EventId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EventDay)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventId, e.EventDayId })
            .HasPrincipalKey(e => new { e.TenantId, e.EventId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.LocationId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Room)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.LocationId, e.RoomId })
            .HasPrincipalKey(e => new { e.TenantId, e.LocationId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Kind)
            .WithMany()
            .HasForeignKey(e => e.KindId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.LocalStartDate, e.LocalStartMinuteOfDay });

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.SortOrder });

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.EventLocationId, e.LocationId });

        builder.HasAnnotation(
            "EventLocationPrivacy:ConsistencyTrigger",
            "event_agenda_items:tenant_id,event_id,event_location_id,location_id,room_id");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_event_agenda_item_end_after_start",
                "end_time > start_time");
            t.HasCheckConstraint(
                "ck_event_agenda_item_local_date_range",
                "local_end_date >= local_start_date");
            t.HasCheckConstraint(
                "ck_event_agenda_item_local_start_minute_range",
                "local_start_minute_of_day BETWEEN 0 AND 1439");
            t.HasCheckConstraint(
                "ck_event_agenda_item_local_end_minute_range",
                "local_end_minute_of_day BETWEEN 0 AND 1439");
            t.HasCheckConstraint(
                "ck_event_agenda_item_local_start_minute_matches_time",
                "local_start_minute_of_day = ((EXTRACT(HOUR FROM local_start_time)::int * 60) + EXTRACT(MINUTE FROM local_start_time)::int)");
            t.HasCheckConstraint(
                "ck_event_agenda_item_local_end_minute_matches_time",
                "local_end_minute_of_day = ((EXTRACT(HOUR FROM local_end_time)::int * 60) + EXTRACT(MINUTE FROM local_end_time)::int)");
            t.HasCheckConstraint(
                "ck_event_agenda_item_room_requires_location",
                "room_id IS NULL OR location_id IS NOT NULL");
            // ELP-230C contraction: a physical venue reference is only legal when it is mediated by an
            // event-scoped EventLocation. This closes the legacy write path that could attach a raw
            // Location without a per-event disclosure policy.
            t.HasCheckConstraint(
                "ck_event_agenda_item_physical_location_requires_event_location",
                "location_id IS NULL OR event_location_id IS NOT NULL");
        });

        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
    }
}
