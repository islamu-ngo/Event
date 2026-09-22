using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventResourceConfiguration : IEntityTypeConfiguration<EventResource>
{
    public void Configure(EntityTypeBuilder<EventResource> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_event_resources_identity", "sort_order >= 0 AND is_deleted IN (0, 1) AND TRIM(title) <> '' AND session_scope_id <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("ck_event_resources_session_scope", "(event_session_id IS NULL AND session_scope_id = event_id) OR (event_session_id IS NOT NULL AND session_scope_id = event_session_id)");
            table.HasCheckConstraint("ck_event_resources_values", "event_resource_kind_id BETWEEN 1 AND 13 AND event_resource_delivery_type_id IN (1, 2) AND publication_state_id BETWEEN 1 AND 4 AND disclosure_mode_id BETWEEN 1 AND 3");
            table.HasCheckConstraint("ck_event_resources_disclosure", "disclosure_mode_id = 1 OR (public_title IS NOT NULL AND TRIM(public_title) <> '')");
            table.HasCheckConstraint("ck_event_resources_alternative", "accessible_alternative_event_resource_id IS NULL OR accessible_alternative_event_resource_id <> id");
            table.HasCheckConstraint("ck_event_resources_availability_start", BoundaryCheck("start"));
            table.HasCheckConstraint("ck_event_resources_availability_end", BoundaryCheck("end"));
            table.HasCheckConstraint("ck_event_resources_availability_order", "availability_absolute_start_utc IS NULL OR availability_absolute_end_utc IS NULL OR availability_absolute_end_utc > availability_absolute_start_utc");
            table.HasCheckConstraint("ck_event_resources_payload", PayloadCheck());
        });

        builder.Property(resource => resource.Id).ValueGeneratedNever();
        builder.Property(resource => resource.Title).IsRequired().HasMaxLength(500);
        builder.Property(resource => resource.PublicTitle).HasMaxLength(500);
        builder.Property(resource => resource.Description).HasMaxLength(5000);
        builder.Property(resource => resource.SensitiveNotes).HasMaxLength(5000);
        builder.Property(resource => resource.LanguageCode).HasMaxLength(35);
        builder.Property(resource => resource.AccessibilityNote).HasMaxLength(2000);
        builder.Property(resource => resource.ExternalDestinationCiphertext).HasColumnType("text").HasMaxLength(8192);
        builder.Property(resource => resource.ExternalDestinationSafeOrigin).HasMaxLength(2048);
        builder.Property(resource => resource.IsDeleted).HasConversion<int>().HasDefaultValue(0);
        builder.Property(resource => resource.ConcurrencyStamp).IsConcurrencyToken();
        builder.Ignore(resource => resource.Availability);

        builder.HasAlternateKey(resource => new { resource.TenantId, resource.Id });
        builder.HasAlternateKey(resource => new { resource.TenantId, resource.EventId, resource.Id });
        builder.HasAlternateKey(resource => new
        {
            resource.TenantId,
            resource.EventId,
            resource.Id,
            resource.SessionScopeId
        });

        builder.HasOne<Tenant>().WithMany()
            .HasForeignKey(resource => resource.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany()
            .HasForeignKey(resource => new { resource.TenantId, resource.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventSession>().WithMany()
            .HasForeignKey(resource => new { resource.TenantId, resource.EventId, resource.EventSessionId })
            .HasPrincipalKey(session => new { session.TenantId, session.EventId, session.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventResourceKind>().WithMany()
            .HasForeignKey(resource => resource.EventResourceKindId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventResourceDeliveryType>().WithMany()
            .HasForeignKey(resource => resource.EventResourceDeliveryTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StorageObject>().WithMany()
            .HasForeignKey(resource => new { resource.TenantId, resource.StorageObjectId })
            .HasPrincipalKey(storage => new { storage.TenantId, storage.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventResource>().WithMany()
            .HasForeignKey(resource => new
            {
                resource.TenantId,
                resource.EventId,
                resource.AccessibleAlternativeEventResourceId
            })
            .HasPrincipalKey(resource => new { resource.TenantId, resource.EventId, resource.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(resource => resource.AudienceRules).WithOne()
            .HasForeignKey(rule => new
            {
                rule.TenantId,
                rule.EventId,
                rule.EventResourceId,
                rule.ResourceSessionScopeId
            })
            .HasPrincipalKey(resource => new
            {
                resource.TenantId,
                resource.EventId,
                resource.Id,
                resource.SessionScopeId
            })
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(resource => resource.AudienceRules)
            .HasField("_audienceRules")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(resource => new
        {
            resource.TenantId,
            resource.EventId,
            resource.IsDeleted,
            resource.SortOrder,
            resource.Id
        });
        builder.HasIndex(resource => new { resource.TenantId, resource.StorageObjectId })
            .IsUnique();
    }

    private static string BoundaryCheck(string boundary) =>
        $"(availability_absolute_{boundary}_utc IS NULL AND availability_{boundary}_anchor_id IS NULL AND availability_{boundary}_offset_ticks IS NULL) OR " +
        $"(availability_absolute_{boundary}_utc IS NOT NULL AND availability_{boundary}_anchor_id IS NULL AND availability_{boundary}_offset_ticks IS NULL) OR " +
        $"(availability_absolute_{boundary}_utc IS NULL AND availability_{boundary}_anchor_id IS NOT NULL AND availability_{boundary}_anchor_id BETWEEN 1 AND 4 AND availability_{boundary}_offset_ticks IS NOT NULL)";

    private static string PayloadCheck()
    {
        const string empty = "(storage_object_id IS NULL AND external_destination_ciphertext IS NULL AND external_destination_protection_version IS NULL AND external_destination_safe_origin IS NULL)";
        const string stored = "(event_resource_delivery_type_id = 1 AND storage_object_id IS NOT NULL AND external_destination_ciphertext IS NULL AND external_destination_protection_version IS NULL AND external_destination_safe_origin IS NULL)";
        const string external = "(event_resource_delivery_type_id = 2 AND storage_object_id IS NULL AND external_destination_ciphertext IS NOT NULL AND TRIM(external_destination_ciphertext) <> '' AND external_destination_protection_version IS NOT NULL AND external_destination_protection_version > 0 AND external_destination_safe_origin IS NOT NULL AND TRIM(external_destination_safe_origin) <> '')";
        return $"(is_deleted = 1 AND {empty}) OR (is_deleted = 0 AND ((publication_state_id IN (1, 4) AND ({empty} OR {stored} OR {external})) OR (publication_state_id IN (2, 3) AND ({stored} OR {external}))))";
    }
}
