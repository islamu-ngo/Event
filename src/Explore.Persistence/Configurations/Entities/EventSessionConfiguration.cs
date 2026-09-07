using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventSessionConfiguration : IEntityTypeConfiguration<EventSession>
{
    public void Configure(EntityTypeBuilder<EventSession> builder)
    {
        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.HasAlternateKey(e => new { e.TenantId, e.Id });
        builder.HasAlternateKey(e => new { e.TenantId, e.EventId, e.Id });

        builder.Property(e => e.Title).HasMaxLength(500);
        builder.Property(e => e.Slug).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.Property(e => e.EndTimeType)
            .HasConversion<int>()
            .HasDefaultValue(SessionEndTimeType.Fixed)
            .IsRequired();

        builder.HasOne(e => e.Event)
            .WithMany(e => e.Sessions)
            .HasForeignKey(e => new { e.TenantId, e.EventId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.EventLocation)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventId, e.EventLocationId })
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

        builder.HasOne(e => e.EventSessionKind)
            .WithMany()
            .HasForeignKey(e => e.EventSessionKindId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.EventSessionStatus)
            .WithMany()
            .HasForeignKey(e => e.EventSessionStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EventDay)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventId, e.EventDayId })
            .HasPrincipalKey(e => new { e.TenantId, e.EventId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.LocalStartDate, e.LocalStartMinuteOfDay });

        builder.HasIndex(e => new { e.TenantId, e.LocationId, e.RoomId, e.StartTime, e.EndTime });

        builder.HasIndex(e => new { e.TenantId, e.EventId, e.EventLocationId, e.LocationId });

        builder.HasAnnotation(
            "EventLocationPrivacy:ConsistencyTrigger",
            "event_sessions:tenant_id,event_id,event_location_id,location_id,room_id");

        builder.HasIndex(e => new { e.TenantId, e.EventDayId, e.SortOrder });

        builder.HasIndex(e => e.EventSessionKindId);

        builder.HasIndex(e => e.EventSessionStatusId);

        builder.HasPostgresExclusionConstraint(
            name: "ex_event_session_room_no_overlap",
            usingMethod: "gist",
            elementsSql: """
                tenant_id WITH =,
                location_id WITH =,
                room_id WITH =,
                tstzrange(start_time, end_time, '[)') WITH &&
                """,
            predicateSql: "is_deleted = false AND room_id IS NOT NULL AND start_time IS NOT NULL AND end_time IS NOT NULL",
            preflightConflictExistsSql: """
                SELECT 1
                FROM event_sessions a
                JOIN event_sessions b
                  ON a.tenant_id = b.tenant_id
                 AND a.location_id = b.location_id
                 AND a.room_id = b.room_id
                 AND a.id::text < b.id::text
                WHERE a.is_deleted = false
                  AND b.is_deleted = false
                  AND a.room_id IS NOT NULL
                  AND a.start_time IS NOT NULL
                  AND a.end_time IS NOT NULL
                  AND b.start_time IS NOT NULL
                  AND b.end_time IS NOT NULL
                  AND tstzrange(a.start_time, a.end_time, '[)')
                      && tstzrange(b.start_time, b.end_time, '[)')
                """,
            preflightFailureMessage: "event_sessions contains overlapping active room assignments");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_event_session_end_after_start",
                "end_time IS NULL OR start_time IS NULL OR end_time > start_time");
            t.HasCheckConstraint(
                "ck_event_session_local_date_range",
                "local_end_date IS NULL OR local_start_date IS NULL OR local_end_date >= local_start_date");
            t.HasCheckConstraint(
                "ck_event_session_end_time_type_state",
                "start_time IS NULL OR ((end_time_type = 0 AND end_time IS NOT NULL) OR (end_time_type = 1 AND end_time IS NULL) OR (end_time_type = 2))");
            t.HasCheckConstraint(
                "ck_event_session_local_start_minute_range",
                "local_start_minute_of_day IS NULL OR local_start_minute_of_day BETWEEN 0 AND 1439");
            t.HasCheckConstraint(
                "ck_event_session_local_end_minute_range",
                "local_end_minute_of_day IS NULL OR local_end_minute_of_day BETWEEN 0 AND 1439");
            t.HasCheckConstraint(
                "ck_event_session_local_start_minute_matches_time",
                "local_start_minute_of_day IS NULL OR local_start_time IS NULL OR local_start_minute_of_day = ((EXTRACT(HOUR FROM local_start_time)::int * 60) + EXTRACT(MINUTE FROM local_start_time)::int)");
            t.HasCheckConstraint(
                "ck_event_session_local_end_minute_matches_time",
                "local_end_minute_of_day IS NULL OR local_end_time IS NULL OR local_end_minute_of_day = ((EXTRACT(HOUR FROM local_end_time)::int * 60) + EXTRACT(MINUTE FROM local_end_time)::int)");
            t.HasCheckConstraint(
                "ck_event_session_room_requires_location",
                "room_id IS NULL OR location_id IS NOT NULL");
            // ELP-230C contraction: a physical venue reference is only legal when it is mediated by an
            // event-scoped EventLocation. This closes the legacy write path that could attach a raw
            // Location without a per-event disclosure policy.
            t.HasCheckConstraint(
                "ck_event_session_physical_location_requires_event_location",
                "location_id IS NULL OR event_location_id IS NOT NULL");
        });

        // Optimistic concurrency control (database-agnostic)
        builder.Property(e => e.ConcurrencyStamp)
            .IsConcurrencyToken();
    }
}
