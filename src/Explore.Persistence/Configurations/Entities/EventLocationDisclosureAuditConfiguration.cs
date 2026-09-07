using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventLocationDisclosureAuditConfiguration
    : IEntityTypeConfiguration<EventLocationDisclosureAudit>
{
    public void Configure(EntityTypeBuilder<EventLocationDisclosureAudit> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_event_location_disclosure_audits_policy_step",
                "previous_policy_version >= 0 AND new_policy_version = previous_policy_version + 1");
            table.HasCheckConstraint(
                "ck_event_location_disclosure_audits_field_flags",
                "previous_fields BETWEEN 0 AND 127 AND new_fields BETWEEN 0 AND 127");
            table.HasCheckConstraint(
                "ck_event_location_disclosure_audits_reason",
                "reason BETWEEN 1 AND 5");
            table.HasCheckConstraint(
                "ck_event_location_disclosure_audits_uuid_v7",
                "substring(id::text, 15, 1) = '7' AND substring(id::text, 20, 1) IN ('8', '9', 'a', 'b')");
        });

        builder.Property(item => item.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(item => item.PreviousFields).HasConversion<int>();
        builder.Property(item => item.NewFields).HasConversion<int>();
        builder.Property(item => item.Reason).HasConversion<int>();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(item => item.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventLocation>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.EventLocationId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocationDisclosureAudience>()
            .WithMany()
            .HasForeignKey(item => item.PreviousAudienceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocationDisclosureAudience>()
            .WithMany()
            .HasForeignKey(item => item.NewAudienceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(item => new { item.TenantId, item.EventLocationId, item.NewPolicyVersion })
            .IsUnique();
        builder.HasIndex(item => new { item.TenantId, item.EventLocationId, item.OccurredAtUtc });
    }
}
