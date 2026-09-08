using Explore.Domain;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Privacy.ErasureAuthority.Configurations;

public sealed class EmbeddedPrivacyErasureIntentConfiguration
    : IEntityTypeConfiguration<PrivacyErasureIntent>
{
    public void Configure(EntityTypeBuilder<PrivacyErasureIntent> builder)
    {
        builder.ToTable(RelationalModelNamespace.Prefix + "erasure_intents", table =>
        {
            table.HasCheckConstraint("ck_erasure_intents_sequence", "authority_sequence > 0");
            table.HasCheckConstraint(
                "ck_erasure_intents_intent_uuid_v7",
                "is_legal_hold_pseudonymized = 1 OR substr(intent_id, 15, 1) = '7'");
            table.HasCheckConstraint("ck_erasure_intents_intent_variant", "lower(substr(intent_id, 20, 1)) IN ('8', '9', 'a', 'b')");
            table.HasCheckConstraint("ck_erasure_intents_subject_kind", "subject_kind = 1");
            table.HasCheckConstraint("ck_erasure_intents_subject_nonempty", "subject_id <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("ck_erasure_intents_reason", "reason_code BETWEEN 1 AND 3");
            table.HasCheckConstraint("ck_erasure_intents_policy_version", "policy_version > 0");
            table.HasCheckConstraint("ck_erasure_intents_server_time_order", "recorded_at_utc >= requested_at_utc");
            table.HasCheckConstraint("ck_erasure_intents_retention", "retention_expires_at_utc > recorded_at_utc");
        });

        builder.HasKey(item => item.AuthoritySequence);
        builder.Property(item => item.AuthoritySequence).ValueGeneratedNever();
        builder.HasIndex(item => new { item.IntentId, item.SubjectKind, item.PolicyVersion }).IsUnique();
        builder.Property(item => item.SubjectKind).HasConversion<short>();
        builder.Property(item => item.ReasonCode).HasConversion<short>();
        builder.Property(item => item.IsLegalHoldPseudonymized).HasDefaultValue(false);
        ConfigureUtcTicks(builder.Property(item => item.RequestedAtUtc));
        ConfigureUtcTicks(builder.Property(item => item.RecordedAtUtc));
        ConfigureUtcTicks(builder.Property(item => item.RetentionExpiresAtUtc));
    }

    private static void ConfigureUtcTicks(PropertyBuilder<DateTime> property) =>
        property.HasConversion(
            value => value.Ticks,
            value => new DateTime(value, DateTimeKind.Utc));
}
