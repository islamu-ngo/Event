using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationQualificationConfiguration :
    IEntityTypeConfiguration<ModerationFinding>,
    IEntityTypeConfiguration<ModerationReviewerAssignment>,
    IEntityTypeConfiguration<ModerationPolicy>,
    IEntityTypeConfiguration<ModerationPolicyRule>
{
    public void Configure(EntityTypeBuilder<ModerationFinding> builder)
    {
        ModerationLedgerConfiguration.ConfigureOwner(builder, "moderation_findings");
        builder.Ignore(entity => entity.Subject);
        builder.Ignore(entity => entity.Scope);
        builder.Ignore(entity => entity.Rule);
        builder.Ignore(entity => entity.Reason);
        builder.Ignore(entity => entity.Adjudicator);
        builder.Ignore(entity => entity.Evidence);
        builder.Ignore(entity => entity.ReversedBy);
        ModerationLedgerConfiguration.ConfigureSubject(builder, "adjudicator");
        ModerationLedgerConfiguration.ConfigureSubject(builder, "reversed_by", optional: true);
        builder.Property(entity => entity.RuleCode).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.ReasonCode).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.Justification).HasMaxLength(4000).IsRequired();
        builder.Property(entity => entity.ReversalJustification).HasMaxLength(4000);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(entity => entity.CaseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entity => new
        {
            entity.CaseId,
            entity.RuleOwnerId,
            entity.RuleCode,
            entity.RuleRevision,
            entity.ScopeKind,
            entity.ScopeId,
            entity.AttributionGeneration
        })
            .IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_finding_state",
                "(state = 1 AND revision = 1 AND reversed_at IS NULL AND reversal_reference_id IS NULL " +
                "AND reversed_by_id IS NULL AND reversal_justification IS NULL AND updated_at = finalized_at) " +
                "OR (state = 2 AND revision = 2 AND reversed_at IS NOT NULL AND reversal_reference_id IS NOT NULL " +
                "AND reversed_by_id IS NOT NULL AND reversal_justification IS NOT NULL " +
                "AND reversed_at >= finalized_at AND updated_at = reversed_at)");
            table.HasCheckConstraint("ck_moderation_finding_evidence",
                "case_revision = 1 AND rule_revision > 0 AND reason_revision > 0 AND evidence_recorded_at >= case_created_at " +
                "AND evidence_recorded_at <= finalized_at AND finalized_at >= case_created_at " +
                "AND ((evidence_purpose = 1 AND urgency_qualification_id IS NULL) " +
                "OR (evidence_purpose = 2 AND urgency_qualification_id IS NOT NULL " +
                "AND urgency_qualification_id <> evidence_reference_id))");
            table.HasCheckConstraint("ck_moderation_finding_adjudicator",
                "adjudicator_kind = 1 AND adjudicator_id <> subject_id " +
                "AND adjudicator_origin_trust_domain_id = scope_trust_domain_id");
            table.HasCheckConstraint("ck_moderation_finding_attribution",
                "(attribution_source_id IS NULL AND attribution_generation = 0) OR " +
                "(attribution_source_id IS NOT NULL AND attribution_generation > 0)");
        });
    }

    public void Configure(EntityTypeBuilder<ModerationReviewerAssignment> builder)
    {
        builder.ToTable("moderation_reviewer_assignments");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(entity => entity.CaseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entity => new { entity.TrustDomainId, entity.CaseTenantId, entity.CaseId, entity.ReviewerHumanId });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_reviewer_revision", "revision > 0");
            table.HasCheckConstraint("ck_moderation_reviewer_subject",
                "subject_human_id IS NULL OR reviewer_human_id <> subject_human_id");
            table.HasCheckConstraint("ck_moderation_reviewer_controller",
                "(controller_human_id IS NULL AND identity_evidence_id IS NULL " +
                "AND qualified_by_human_id IS NULL AND qualified_until IS NULL) " +
                "OR (controller_human_id IS NOT NULL AND identity_evidence_id IS NOT NULL " +
                "AND qualified_by_human_id IS NOT NULL AND qualified_until IS NOT NULL " +
                "AND qualified_until > created_at AND (subject_human_id IS NULL OR controller_human_id <> subject_human_id))");
            table.HasCheckConstraint("ck_moderation_reviewer_conflict",
                "(conflict_state = 1 AND conflict_evidence_id IS NULL AND assessed_by_human_id IS NULL) " +
                "OR (conflict_state IN (2, 3) AND controller_human_id IS NOT NULL " +
                "AND conflict_evidence_id IS NOT NULL AND assessed_by_human_id IS NOT NULL " +
                "AND assessed_by_human_id <> reviewer_human_id AND assessed_by_human_id <> controller_human_id " +
                "AND (subject_human_id IS NULL OR assessed_by_human_id <> subject_human_id))");
            table.HasCheckConstraint("ck_moderation_reviewer_withdrawal",
                "(withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) " +
                "OR (withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL " +
                "AND withdrawn_at = updated_at AND withdrawn_at >= created_at)");
        });
    }

    public void Configure(EntityTypeBuilder<ModerationPolicy> builder)
    {
        builder.ToTable("moderation_policies");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Ignore(entity => entity.Jurisdiction);
        builder.Ignore(entity => entity.IsActive);
        ModerationLedgerConfiguration.ConfigureScope(builder, "jurisdiction");
        builder.HasIndex(entity => new { entity.JurisdictionKind, entity.JurisdictionId }).IsUnique();
        builder.HasMany(entity => entity.Rules).WithOne().HasForeignKey(entity => entity.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(entity => entity.Rules).HasField("_rules")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_policy_revision", "revision > 0 AND rule_count > 0");
            table.HasCheckConstraint("ck_moderation_policy_jurisdiction",
                "jurisdiction_id = CASE jurisdiction_kind " +
                "WHEN 1 THEN jurisdiction_trust_domain_id WHEN 2 THEN jurisdiction_solution_id " +
                "WHEN 3 THEN jurisdiction_instance_id WHEN 4 THEN jurisdiction_tenant_id " +
                "WHEN 5 THEN jurisdiction_community_id WHEN 6 THEN jurisdiction_resource_id " +
                "WHEN 7 THEN jurisdiction_event_id END");
        });
    }

    public void Configure(EntityTypeBuilder<ModerationPolicyRule> builder)
    {
        builder.ToTable("moderation_policy_rules");
        builder.HasKey(entity => new { entity.PolicyId, entity.Id });
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Ignore(entity => entity.Rule);
        builder.Ignore(entity => entity.MaximumFiniteDuration);
        builder.Ignore(entity => entity.MaximumReviewDelay);
        builder.Ignore(entity => entity.Capabilities);
        builder.Ignore(entity => entity.ActiveCapabilities);
        builder.Ignore(entity => entity.Purposes);
        builder.Property(entity => entity.RuleCode).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.Meaning).HasMaxLength(4000).IsRequired();
        builder.HasIndex(entity => new { entity.PolicyId, entity.RuleOwnerId, entity.RuleCode, entity.RuleRevision })
            .IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_policy_rule_revision", "policy_revision > 0 AND rule_revision > 0");
            table.HasCheckConstraint("ck_moderation_policy_rule_purpose", "purpose_mask > 0 AND (purpose_mask & 14) = purpose_mask");
            table.HasCheckConstraint("ck_moderation_policy_rule_effect",
                "capability_mask > 0 AND (capability_mask & 511) = capability_mask AND (capability_mask & 1) = 0 " +
                "AND active_capability_mask >= 0 AND (active_capability_mask & capability_mask) = active_capability_mask");
            table.HasCheckConstraint("ck_moderation_policy_rule_terms",
                "maximum_finite_duration_ticks > 0 AND maximum_review_delay_ticks > 0 " +
                "AND minimum_approvals IN (1, 2) AND maximum_approvals IN (1, 2) AND maximum_approvals >= minimum_approvals");
        });
    }
}
