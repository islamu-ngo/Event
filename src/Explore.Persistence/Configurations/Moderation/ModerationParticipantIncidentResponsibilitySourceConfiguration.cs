using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationParticipantIncidentResponsibilitySourceConfiguration :
    IEntityTypeConfiguration<ModerationParticipantIncidentResponsibilitySource>
{
    public void Configure(EntityTypeBuilder<ModerationParticipantIncidentResponsibilitySource> builder)
    {
        builder.ToTable("moderation_participant_incident_responsibility_sources");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.AssessmentDigest).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Ignore(row => row.ProtectedAssessment);
        builder.Property<byte[]>("protectedAssessment").HasField("protectedAssessment")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("protected_assessment").HasMaxLength(131072).IsRequired();

        // UTC ticks preserve every 100ns deadline on all engines and permit exact, portable
        // retention arithmetic without timestamp subtraction or provider-specific SQL.
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.ClrType == typeof(DateTimeOffset)).ToArray())
            builder.Property<DateTimeOffset>(property.Name).HasConversion(
                instant => instant.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
        builder.Property(row => row.WithdrawnAt).HasConversion(
            instant => instant.HasValue ? instant.Value.UtcTicks : (long?)null,
            ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : (DateTimeOffset?)null);

        builder.HasIndex(row => new { row.TenantId, row.TrustDomainId, row.OccurrenceCaseId, row.SubjectId });
        builder.HasIndex(row => new { row.TenantId, row.TrustDomainId, row.PurgeAt });
        builder.HasOne<ModerationHostBinding>().WithMany()
            .HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany()
            .HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany()
            .HasForeignKey(row => row.OccurrenceCaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseTargetSource>().WithMany()
            .HasForeignKey(row => row.EvidenceSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseNarrative>().WithMany()
            .HasForeignKey(row => row.OriginalNarrativeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany()
            .HasForeignKey(row => row.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventSession>().WithMany()
            .HasForeignKey(row => row.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrder>().WithMany()
            .HasForeignKey(row => row.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationParticipant>().WithMany()
            .HasForeignKey(row => row.ParticipantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(row => row.SubjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(row => row.AssessorHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(row => row.ReadIssuerHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany()
            .HasForeignKey(row => row.WithdrawnByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseReadGrant>().WithMany()
            .HasForeignKey(row => row.ReadGrantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseTargetPolicy>().WithMany()
            .HasForeignKey(row => row.QualificationPolicyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseNarrativePolicySource>().WithMany()
            .HasForeignKey(row => new { row.CustodySourceId, row.HostBindingId, row.TrustDomainId, row.TenantId })
            .HasPrincipalKey(row => new { row.Id, row.HostBindingId, row.TrustDomainId, row.TenantId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCaseNarrativeAdoption>().WithMany()
            .HasForeignKey(row => new
            {
                row.CustodyAdoptionId,
                row.HostBindingId,
                row.TrustDomainId,
                row.TenantId,
                row.CustodySourceId
            })
            .HasPrincipalKey(row => new { row.Id, row.HostBindingId, row.TrustDomainId, row.TenantId, row.SourceId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_incident_assessment_purpose",
                "target_kind = 2 AND purpose = 2 AND subject_kind = 1 " +
                "AND subject_id <> assessor_human_id AND subject_id <> read_issuer_human_id");
            table.HasCheckConstraint("ck_incident_assessment_revisions",
                "evidence_case_revision > 0 AND occurrence_case_revision >= evidence_case_revision " +
                "AND read_grant_revision = 1 AND qualification_policy_revision > 0 " +
                "AND custody_source_revision = 1 AND custody_adoption_revision = 1");
            table.HasCheckConstraint("ck_incident_assessment_generations",
                "custody_source_writer_generation > 0 AND custody_source_writer_generation < 9223372036854775807 " +
                "AND custody_adoption_writer_generation > custody_source_writer_generation " +
                "AND custody_adoption_writer_generation < 9223372036854775807 AND custody_adoption_id <> custody_source_id " +
                "AND evidence_source_version <> '00000000-0000-0000-0000-000000000000' " +
                "AND event_version <> '00000000-0000-0000-0000-000000000000' " +
                "AND session_generation <> '00000000-0000-0000-0000-000000000000' " +
                "AND order_generation <> '00000000-0000-0000-0000-000000000000' " +
                "AND participant_generation <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("ck_incident_assessment_window",
                "incident_window_from > 0 AND incident_window_until > incident_window_from " +
                "AND incident_window_until <= qualified_at");
            table.HasCheckConstraint("ck_incident_assessment_finite_deadlines",
                "qualified_at > 0 AND valid_until > qualified_at AND valid_until <= purge_at " +
                "AND purge_at <= original_narrative_purge_at AND original_narrative_purge_at < 3155378975999999999 " +
                "AND purge_at <= read_grant_expires_at AND read_grant_expires_at < 3155378975999999999 " +
                "AND purge_at <= custody_adoption_valid_until AND custody_adoption_valid_until <= custody_source_valid_until " +
                "AND custody_source_valid_until < 3155378975999999999");
            table.HasCheckConstraint("ck_incident_assessment_lifetime_limits",
                "maximum_qualification_lifetime_ticks > 0 AND maximum_qualification_lifetime_ticks < 9223372036854775807 " +
                "AND maximum_retention_ticks > 0 AND maximum_retention_ticks < 9223372036854775807 " +
                "AND valid_until - qualified_at <= maximum_qualification_lifetime_ticks " +
                "AND purge_at - qualified_at <= maximum_retention_ticks");
            // PortableRelationalModelPolicy supplies the provider's string-length spelling.
            table.HasCheckConstraint("ck_incident_assessment_digest", "length(assessment_digest) = 64");
            table.HasCheckConstraint("ck_incident_assessment_withdrawal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL AND withdrawal_operation_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_at >= qualified_at " +
                "AND withdrawn_at < 3155378975999999999 AND withdrawn_by_human_id IS NOT NULL " +
                "AND withdrawal_operation_id IS NOT NULL " +
                "AND withdrawal_operation_id <> '00000000-0000-0000-0000-000000000000')");
        });

        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationParticipantIncidentResponsibilitySource.Revision)
                or nameof(ModerationParticipantIncidentResponsibilitySource.WithdrawalOperationId)
                or nameof(ModerationParticipantIncidentResponsibilitySource.WithdrawnByHumanId)
                or nameof(ModerationParticipantIncidentResponsibilitySource.WithdrawnAt))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
