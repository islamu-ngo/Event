using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class LocationPrivacyCorrectionReceiptConfiguration
    : IEntityTypeConfiguration<LocationPrivacyCorrectionReceipt>
{
    public void Configure(EntityTypeBuilder<LocationPrivacyCorrectionReceipt> builder)
    {
        builder.ToTable("location_privacy_correction_receipts", table =>
        {
            table.HasCheckConstraint("ck_location_correction_policy", "policy_version > 0");
            table.HasCheckConstraint("ck_location_correction_source",
                "(event_type = 'location.privacy.corrected' AND disclosure_audit_id IS NOT NULL "
                + "AND erasure_intent_id IS NULL AND authority_sequence IS NULL AND location_id IS NULL) OR "
                + "(event_type = 'LocationPrivacyCorrectionRequested' AND disclosure_audit_id IS NULL "
                + "AND erasure_intent_id IS NOT NULL AND authority_sequence IS NOT NULL "
                + "AND authority_sequence > 0 AND location_id IS NOT NULL)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.EventType).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => new { row.TenantId, row.EventLocationId, row.PolicyVersion, row.EventType }).IsUnique();
        // This receipt intentionally has no aggregate, audit, PII or outbox cascade.
    }
}
