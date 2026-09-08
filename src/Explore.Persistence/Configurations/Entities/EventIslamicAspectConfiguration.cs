// The aspect's Id is both its PK and the FK to Event.Id (1:1 relationship).

namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class EventIslamicAspectConfiguration : IEntityTypeConfiguration<EventIslamicAspect>
{
    public void Configure(EntityTypeBuilder<EventIslamicAspect> builder)
    {
        // Primary key - shared with Event (no UUID v7 generation here, uses Event.Id)
        builder.HasKey(e => e.Id);

        // 1:1 relationship with Event using shared primary key pattern
        builder.HasOne(e => e.Event)
            .WithOne(e => e.IslamicAspect)
            .HasForeignKey<EventIslamicAspect>(e => e.Id)
            .OnDelete(DeleteBehavior.Cascade);

        // Madhab relationship
        builder.HasOne(e => e.Madhab)
            .WithMany()
            .HasForeignKey(e => e.MadhabId)
            .OnDelete(DeleteBehavior.SetNull);

        // Prayer time scheduling
        builder.Property(e => e.ReferencePrayer)
            .HasConversion<int?>();

        builder.Property(e => e.PrayerTimeOffset)
            .HasDefaultValue(null);

        // Gender segregation mode
        builder.Property(e => e.GenderMode)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(GenderSegregationMode.Mixed);

        builder.Property(e => e.IncludesQuranRecitation)
            .IsRequired()
            .HasDefaultValue(false);

        // Primary language relationship
        builder.HasOne(e => e.PrimaryLanguage)
            .WithMany()
            .HasForeignKey(e => e.PrimaryLanguageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
