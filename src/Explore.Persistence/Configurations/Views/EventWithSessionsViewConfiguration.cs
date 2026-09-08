using Explore.Domain.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Views;

public sealed class EventWithSessionsViewConfiguration : IEntityTypeConfiguration<EventWithSessionsView>
{
    public void Configure(EntityTypeBuilder<EventWithSessionsView> builder)
    {
        builder.HasNoKey();
        builder.ToView("vw_event_with_sessions");

        builder.Property(e => e.Description)
            .IsRequired(false);

        builder.Property(e => e.EndAt)
            .IsRequired(false);

        builder.Property(e => e.UpdatedAt)
            .IsRequired(false);

        builder.Property(e => e.IslamicTheme)
            .IsRequired(false);

        builder.Property(e => e.Madhab)
            .IsRequired(false);

        builder.Property(e => e.IsRamadan)
            .IsRequired(false);

        builder.Property(e => e.PrayerAware)
            .IsRequired(false);

        builder.Property(e => e.TechStack)
            .IsRequired(false);

        builder.Property(e => e.DifficultyLevel)
            .IsRequired(false);

        builder.Property(e => e.TargetAudience)
            .IsRequired(false);

        builder.Property(e => e.FirstSessionStartAt)
            .IsRequired(false);

        builder.Property(e => e.LastSessionEndAt)
            .IsRequired(false);

        builder.Property(e => e.AggregatedSessionIslamicThemes)
            .IsRequired(false);
    }
}
