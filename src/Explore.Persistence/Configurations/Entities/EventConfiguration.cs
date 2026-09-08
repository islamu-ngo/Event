using System;
using System.Collections.Generic;
using System.Text;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Seed;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.UseTptMappingStrategy();

        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasAlternateKey(e => new { e.TenantId, e.Id });
        builder.Property(e => e.TotalViews).HasDefaultValue(0);

        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Subtitle).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(150);
        builder.Property(e => e.Content).HasMaxLength(5000);
        builder.Property(e => e.Slug).HasMaxLength(200);
        builder.Property(e => e.PublicCode).HasMaxLength(12).IsRequired();
        builder.Property(e => e.Timezone).HasMaxLength(100);
        builder.Property(e => e.EventTimeZoneId).HasMaxLength(100);
        builder.Property(e => e.ProvenanceSource).HasMaxLength(100);
        builder.Property(e => e.ProvenanceExternalId).HasMaxLength(200);
        builder.Property(e => e.SourcePublisherName).HasMaxLength(200);

        builder.HasOne(e => e.EventType)
            .WithMany()
            .HasForeignKey(e => e.EventTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.AudienceGender)
            .WithMany()
            .HasForeignKey(e => e.AudienceGenderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.AudienceAge)
            .WithMany()
            .HasForeignKey(e => e.AudienceAgeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Actor)
            .WithMany()
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EventProvenanceType)
            .WithMany()
            .HasForeignKey(e => e.EventProvenanceTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.SubmittedByUser)
            .WithMany()
            .HasForeignKey(e => e.SubmittedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.OrganizerActor)
            .WithMany()
            .HasForeignKey(e => e.OrganizerActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FeaturedImage)
            .WithMany()
            .HasForeignKey(e => e.FeaturedImageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Madhab)
            .WithMany()
            .HasForeignKey(e => e.MadhabId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.VisibilityType)
            .WithMany()
            .HasForeignKey(e => e.VisibilityTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EventStatus)
            .WithMany()
            .HasForeignKey(e => e.EventStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EventFormat)
            .WithMany()
            .HasForeignKey(e => e.EventFormatId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.RegistrationPolicy)
            .WithMany()
            .HasForeignKey(e => e.RegistrationPolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.AtprotoRecord)
            .WithMany()
            .HasForeignKey(e => e.AtprotoRecordId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Navigation(e => e.TicketCatalogVersions)
            .HasField("_ticketCatalogVersions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(e => e.CapacityPools)
            .HasField("_capacityPools")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Configure aspect navigation properties (shared PK pattern - no FK needed)
        builder.HasOne(e => e.IslamicAspect)
            .WithOne(a => a.Event)
            .HasForeignKey<EventIslamicAspect>(a => a.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.TechAspect)
            .WithOne(a => a.Event)
            .HasForeignKey<EventTechAspect>(a => a.Id)
            .OnDelete(DeleteBehavior.Cascade);

        // Per-event appearance
        builder.Property(e => e.BackgroundColor).HasMaxLength(50);
        builder.Property(e => e.BackgroundEffect).HasMaxLength(50);

        builder.HasOne(e => e.BackgroundImage)
            .WithMany()
            .HasForeignKey(e => e.BackgroundImageId)
            .OnDelete(DeleteBehavior.SetNull);

        // ===== Performance Indexes =====

        // Primary listing query: active published events per tenant
        builder.HasIndex(e => new { e.TenantId, e.IsDeleted, e.EventStatusId });

        // Organization event listing: events by org, sorted by date
        builder.HasIndex(e => new { e.TenantId, e.ActorId, e.CreatedAt })
            .IsDescending(false, false, true);

        // Date range queries: upcoming/past events
        builder.HasIndex(e => new { e.TenantId, e.FirstSessionDate, e.LastSessionDate });

        // Event type filtering
        builder.HasIndex(e => new { e.TenantId, e.EventTypeId });

        // Slug lookup (for URL-friendly event access)
        builder.HasIndex(e => new { e.TenantId, e.Slug });

        builder.HasIndex(e => new { e.TenantId, e.PublicCode })
            .IsUnique();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_event_session_date_range",
                "first_session_date IS NULL OR last_session_date IS NULL OR first_session_date <= last_session_date");
            t.HasCheckConstraint(
                "ck_event_session_start_utc_range",
                "first_session_start_utc IS NULL OR last_session_start_utc IS NULL OR first_session_start_utc <= last_session_start_utc");
            t.HasCheckConstraint(
                "ck_event_time_zone_id_not_blank",
                "event_time_zone_id IS NULL OR length(btrim(event_time_zone_id)) > 0");
        });

        // Optimistic concurrency control (database-agnostic)
        builder.Property(e => e.ConcurrencyStamp)
            .IsConcurrencyToken();

        // NOTE: Business entity seed data moved to DatabaseSeeder for conditional (Development-only) seeding.
        // See Explore.Persistence/Seed/DatabaseSeeder.cs and SeedData.cs
    }
}
