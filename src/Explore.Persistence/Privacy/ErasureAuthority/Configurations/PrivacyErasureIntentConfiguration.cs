using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Privacy.ErasureAuthority.Configurations;

public sealed class PrivacyErasureIntentConfiguration
    : IEntityTypeConfiguration<PrivacyErasureIntent>
{
    public void Configure(EntityTypeBuilder<PrivacyErasureIntent> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_privacy_erasure_intents_sequence", "authority_sequence > 0");
            table.HasCheckConstraint(
                "ck_privacy_erasure_intents_intent_uuid_v7",
                "is_legal_hold_pseudonymized OR substring(intent_id::text, 15, 1) = '7'");
            table.HasCheckConstraint("ck_privacy_erasure_intents_intent_rfc4122_variant", "substring(intent_id::text, 20, 1) IN ('8', '9', 'a', 'b')");
            table.HasCheckConstraint("ck_privacy_erasure_intents_subject_kind", "subject_kind = 1");
            table.HasCheckConstraint("ck_privacy_erasure_intents_subject_nonempty", "subject_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_privacy_erasure_intents_reason", "reason_code BETWEEN 1 AND 3");
            table.HasCheckConstraint("ck_privacy_erasure_intents_policy_version", "policy_version > 0");
            table.HasCheckConstraint("ck_privacy_erasure_intents_server_time_order", "recorded_at_utc >= requested_at_utc");
            table.HasCheckConstraint("ck_privacy_erasure_intents_retention", "retention_expires_at_utc > recorded_at_utc");
        });

        builder.HasKey(item => item.AuthoritySequence);
        builder.Property(item => item.AuthoritySequence).ValueGeneratedNever();
        builder.HasIndex(item => new { item.IntentId, item.SubjectKind, item.PolicyVersion })
            .IsUnique();
        builder.Property(item => item.SubjectKind).HasConversion<short>();
        builder.Property(item => item.ReasonCode).HasConversion<short>();
        builder.Property(item => item.IsLegalHoldPseudonymized).HasDefaultValue(false);
        builder.Property(item => item.RetentionExpiresAtUtc)
            .HasDefaultValueSql("'infinity'::timestamp with time zone");
    }
}
