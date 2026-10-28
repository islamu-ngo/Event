using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventResourceModerationQuarantineConfiguration : IEntityTypeConfiguration<EventResourceModerationQuarantine>
{
    public void Configure(EntityTypeBuilder<EventResourceModerationQuarantine> builder)
    {
        builder.ToTable("event_resource_moderation_quarantines", table =>
        {
            table.HasCheckConstraint("ck_event_resource_quarantines_ordinal", "binding_ordinal >= 0 AND binding_ordinal < 64");
            table.HasCheckConstraint("ck_event_resource_quarantines_material",
                "material_version_id <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("ck_event_resource_quarantines_cause",
                "effect_id <> '00000000-0000-0000-0000-000000000000'");
        });
        builder.HasKey(row => row.Id);
        builder.HasAlternateKey(row => new { row.TenantId, row.Id });
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.AppliedAt).IsRequired();
        builder.Property(row => row.ReleaseId).IsConcurrencyToken();
        builder.HasOne<EventResourceModerationQuarantineRelease>().WithMany()
            .HasForeignKey(row => new { row.TenantId, row.ReleaseId })
            .HasPrincipalKey(row => new { row.TenantId, row.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.TenantId, row.EventResourceId, row.EffectId, row.BindingOrdinal }).IsUnique();
        builder.HasOne<EventResource>().WithMany(resource => resource.ModerationQuarantines)
            .HasForeignKey(row => new { row.TenantId, row.EventId, row.EventResourceId })
            .HasPrincipalKey(resource => new { resource.TenantId, resource.EventId, resource.Id })
            .OnDelete(DeleteBehavior.Restrict);
        // No FK to the original private capture or global effect ledger: these rows are the
        // operational resource owner, not retained notice explanations or completion receipts.
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
                     property.Name != nameof(EventResourceModerationQuarantine.ReleaseId)))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
