using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationScheduleRiskReviewConfiguration : IEntityTypeConfiguration<ModerationScheduleRiskReview>
{
    public void Configure(EntityTypeBuilder<ModerationScheduleRiskReview> builder)
    {
        builder.ToTable("moderation_schedule_risk_reviews");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.Ignore(entity => entity.Subject);
        builder.Ignore(entity => entity.SourceScope);
        builder.Ignore(entity => entity.SourceRule);
        builder.Ignore(entity => entity.EventScope);
        builder.Ignore(entity => entity.OriginalCoordinates);
        builder.Ignore(entity => entity.Coordinates);
        builder.Ignore(entity => entity.Owner);
        builder.Ignore(entity => entity.EscalationOwner);
        builder.Ignore(entity => entity.WithdrawnBy);
        builder.Property(entity => entity.SourceRuleCode).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.PolicyRuleCode).HasMaxLength(100).IsRequired();
        ModerationLedgerConfiguration.ConfigureScope(builder, "source_scope");
        ModerationLedgerConfiguration.ConfigureScope(builder, "event_scope");
        ModerationLedgerConfiguration.ConfigureSubject(builder, "subject");
        ModerationLedgerConfiguration.ConfigureSubject(builder, "owner");
        ModerationLedgerConfiguration.ConfigureSubject(builder, "escalation_owner");
        builder.HasOne<ModerationSanction>().WithMany().HasForeignKey(entity => entity.SourceSanctionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationFinding>().WithMany().HasForeignKey(entity => entity.AcceptedFindingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationReviewerAssignment>().WithMany().HasForeignKey(entity => entity.AcceptedReviewerAssignmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entity => new
        {
            entity.SourceSanctionId,
            entity.EventScopeEventId,
            entity.SessionId,
            entity.ParticipantId,
            entity.OriginalScheduleGeneration,
            entity.OriginalParticipantGeneration,
            entity.OriginalPublicationId
        }).IsUnique();
        builder.HasIndex(entity => new { entity.EventScopeTrustDomainId, entity.EventScopeTenantId, entity.State, entity.DueAt });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_schedule_risk_ancestry",
                "subject_kind = 1 AND owner_kind = 1 AND escalation_owner_kind = 1 AND event_scope_kind = 7 " +
                "AND event_scope_tenant_id IS NOT NULL AND source_sanction_revision > 0 AND revision > 0 " +
                "AND source_rule_revision > 0 AND original_policy_revision > 0 AND policy_revision >= original_policy_revision " +
                "AND original_policy_rule_revision > 0 AND policy_rule_revision >= original_policy_rule_revision " +
                "AND subject_origin_trust_domain_id = source_scope_trust_domain_id " +
                "AND owner_origin_trust_domain_id = source_scope_trust_domain_id " +
                "AND escalation_owner_origin_trust_domain_id = source_scope_trust_domain_id " +
                "AND event_scope_trust_domain_id = source_scope_trust_domain_id " +
                "AND (source_scope_solution_id IS NULL OR source_scope_solution_id = event_scope_solution_id) " +
                "AND (source_scope_instance_id IS NULL OR source_scope_instance_id = event_scope_instance_id) " +
                "AND (source_scope_tenant_id IS NULL OR source_scope_tenant_id = event_scope_tenant_id) " +
                "AND source_scope_community_id IS NULL AND source_scope_resource_id IS NULL " +
                "AND (source_scope_event_id IS NULL OR source_scope_event_id = event_scope_event_id)");
            table.HasCheckConstraint("ck_moderation_schedule_risk_identity",
                "source_sanction_id <> '00000000-0000-0000-0000-000000000000' " +
                "AND source_decision_id <> '00000000-0000-0000-0000-000000000000' " +
                "AND session_id <> '00000000-0000-0000-0000-000000000000' " +
                "AND participant_id <> '00000000-0000-0000-0000-000000000000' " +
                "AND policy_id <> '00000000-0000-0000-0000-000000000000' " +
                "AND original_schedule_generation <> '00000000-0000-0000-0000-000000000000' " +
                "AND schedule_generation <> '00000000-0000-0000-0000-000000000000' " +
                "AND original_participant_generation <> '00000000-0000-0000-0000-000000000000' " +
                "AND participant_generation <> '00000000-0000-0000-0000-000000000000' " +
                "AND original_publication_id <> '00000000-0000-0000-0000-000000000000' " +
                "AND publication_id <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("ck_moderation_schedule_risk_interval",
                "updated_at >= created_at AND original_due_at > created_at AND due_at > created_at " +
                "AND (original_sanction_ends_at IS NULL OR original_sanction_ends_at > original_sanction_starts_at)");
            table.HasCheckConstraint("ck_moderation_schedule_risk_state",
                "state BETWEEN 1 AND 4 AND ((state = 4 AND updated_at >= due_at) OR (state <> 4 AND updated_at < due_at)) " +
                "AND (revision <> 1 OR (state = 1 AND updated_at = created_at AND due_at = original_due_at " +
                "AND schedule_generation = original_schedule_generation AND participant_generation = original_participant_generation " +
                "AND policy_revision = original_policy_revision AND publication_id = original_publication_id " +
                "AND policy_rule_revision = original_policy_rule_revision))");
            table.HasCheckConstraint("ck_moderation_schedule_risk_terminal",
                "((state = 2 AND accepted_intent_id IS NOT NULL AND accepted_finding_id IS NOT NULL " +
                "AND accepted_finding_revision IS NOT NULL AND accepted_finding_revision > 0 " +
                "AND accepted_reviewer_assignment_id IS NOT NULL AND accepted_reviewer_assignment_revision IS NOT NULL " +
                "AND accepted_reviewer_assignment_revision > 0) " +
                "OR (state <> 2 AND accepted_intent_id IS NULL AND accepted_finding_id IS NULL " +
                "AND accepted_finding_revision IS NULL AND accepted_reviewer_assignment_id IS NULL " +
                "AND accepted_reviewer_assignment_revision IS NULL)) " +
                "AND ((state = 3 AND withdrawal_intent_id IS NOT NULL AND withdrawn_by_human_id IS NOT NULL " +
                "AND withdrawn_by_human_id = owner_id) " +
                "OR (state <> 3 AND withdrawal_intent_id IS NULL AND withdrawn_by_human_id IS NULL))");
        });
    }
}
