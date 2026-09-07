namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class EventSessionIslamicAspectConfiguration : IEntityTypeConfiguration<EventSessionIslamicAspect>
{
    public void Configure(EntityTypeBuilder<EventSessionIslamicAspect> builder)
    {
        builder.HasKey(e => e.EventSessionId);

        builder.HasOne(e => e.EventSession)
            .WithOne(e => e.IslamicAspect)
            .HasForeignKey<EventSessionIslamicAspect>(e => e.EventSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.StartTimeType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.ReferencePrayer)
            .HasConversion<int?>();

        builder.Property(e => e.EndReferencePrayer)
            .HasConversion<int?>();

        builder.Property(e => e.RequiresWudu)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(e => e.RitualRequirementsJson)
            .HasColumnType("jsonb");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_event_session_islamic_aspect_start_time_state",
                "((start_time_type = 0 AND reference_prayer IS NULL AND offset_minutes IS NULL) OR (start_time_type = 1 AND reference_prayer IS NOT NULL AND offset_minutes IS NOT NULL))");
            t.HasCheckConstraint(
                "ck_event_session_islamic_aspect_offset_range",
                "offset_minutes IS NULL OR offset_minutes BETWEEN -180 AND 180");
            t.HasCheckConstraint(
                "ck_event_session_islamic_aspect_reference_prayer_range",
                "reference_prayer IS NULL OR reference_prayer BETWEEN 1 AND 6");
            t.HasCheckConstraint(
                "ck_event_session_islamic_aspect_end_time_state",
                "((end_reference_prayer IS NULL AND end_offset_minutes IS NULL) OR (end_reference_prayer IS NOT NULL AND end_offset_minutes IS NOT NULL))");
            t.HasCheckConstraint(
                "ck_event_session_islamic_aspect_end_offset_range",
                "end_offset_minutes IS NULL OR end_offset_minutes BETWEEN -180 AND 180");
            t.HasCheckConstraint(
                "ck_event_session_islamic_aspect_end_reference_prayer_range",
                "end_reference_prayer IS NULL OR end_reference_prayer BETWEEN 1 AND 6");
        });
    }
}
