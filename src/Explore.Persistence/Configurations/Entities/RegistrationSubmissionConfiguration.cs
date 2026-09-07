using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationSubmissionConfiguration : IEntityTypeConfiguration<RegistrationSubmission>
{
    public void Configure(EntityTypeBuilder<RegistrationSubmission> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_registration_submissions_provider_tuple",
                "(registration_provider_binding_id IS NULL AND provider_mapping_revision_hash IS NULL AND provider_submission_id IS NULL AND provider_response_revision IS NULL) OR " +
                "(registration_provider_binding_id IS NOT NULL AND provider_mapping_revision_hash IS NOT NULL AND ((provider_submission_id IS NULL AND provider_response_revision IS NULL) OR (provider_submission_id IS NOT NULL AND provider_response_revision IS NOT NULL)))");
            table.HasCheckConstraint("ck_registration_submissions_finalization_shape",
                $"(status_id = {(int)RegistrationSubmissionStatusEnum.EvidenceOnly} AND is_finalizable = false AND attempt_consumption_claim_id IS NULL AND finalized_at IS NULL) OR " +
                $"(status_id = {(int)RegistrationSubmissionStatusEnum.Received} AND is_finalizable = true AND attempt_consumption_claim_id IS NOT NULL AND finalized_at IS NULL) OR " +
                $"(status_id = {(int)RegistrationSubmissionStatusEnum.Finalized} AND is_finalizable = true AND attempt_consumption_claim_id IS NOT NULL AND finalized_at IS NOT NULL)");
        });
        builder.Property(submission => submission.Id).ValueGeneratedNever();
        builder.Property(submission => submission.BusinessDeduplicationKey).IsRequired().HasMaxLength(71);
        builder.Property(submission => submission.ReceivedEvidenceHash)
            .HasConversion(hash => hash.Value, value => RegistrationEvidenceHash.Create(value)).HasMaxLength(44).IsRequired();
        builder.Property(submission => submission.HttpIdempotencyKeyHash)
            .HasConversion(hash => hash == null ? null : hash.Value,
                value => value == null ? null : RegistrationTransportIdempotencyHash.Create(value)).HasMaxLength(44);
        builder.Property(submission => submission.ProviderMappingRevisionHash)
            .HasConversion(hash => hash == null ? null : hash.Value,
                value => value == null ? null : RegistrationEvidenceHash.Create(value)).HasMaxLength(44);
        builder.Property(submission => submission.ProviderSubmissionId).HasMaxLength(200);
        builder.Property(submission => submission.ProviderResponseRevision).HasMaxLength(200);
        builder.Property(submission => submission.ProviderSubjectId).HasMaxLength(200);
        builder.Property(submission => submission.ProviderCorrelationId).HasMaxLength(200);
        builder.Property(submission => submission.ReceivedAt).IsRequired();
        builder.Property(submission => submission.CreatedAt).IsRequired();
        builder.Property(submission => submission.IsDeleted).HasDefaultValue(false);
        builder.Property(submission => submission.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(submission => new { submission.TenantId, submission.EventId, submission.Id });
        builder.HasAlternateKey(submission => new
        {
            submission.TenantId,
            submission.EventId,
            submission.RegistrationOrderId,
            submission.RegistrationWorkflowId,
            submission.RegistrationRequirementId,
            submission.RegistrationFormId,
            submission.RegistrationFormVersionId,
            submission.RegistrationAttemptId,
            submission.Id
        });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(submission => submission.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany().HasForeignKey(submission => new { submission.TenantId, submission.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationAttempt>().WithMany()
            .HasForeignKey(submission => new
            {
                submission.TenantId,
                submission.EventId,
                submission.RegistrationOrderId,
                submission.RegistrationWorkflowId,
                submission.RegistrationRequirementId,
                submission.RegistrationChannelId,
                submission.RegistrationFormId,
                submission.RegistrationFormVersionId,
                submission.RegistrationAttemptId
            })
            .HasPrincipalKey(attempt => new
            {
                attempt.TenantId,
                attempt.EventId,
                attempt.RegistrationOrderId,
                attempt.RegistrationWorkflowId,
                attempt.RegistrationRequirementId,
                attempt.RegistrationChannelId,
                attempt.RegistrationFormId,
                attempt.RegistrationFormVersionId,
                attempt.Id
            }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationAttemptStatus>().WithMany().HasForeignKey(submission => submission.AttemptStatusAtReceiptId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(submission => submission.Status).WithMany().HasForeignKey(submission => submission.StatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(submission => submission.Revisions).WithOne()
            .HasForeignKey(revision => new { revision.TenantId, revision.EventId, revision.RegistrationSubmissionId })
            .HasPrincipalKey(submission => new { submission.TenantId, submission.EventId, submission.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(submission => new
        { submission.TenantId, submission.RegistrationAttemptId, submission.BusinessDeduplicationKey })
            .IsUnique().HasFilter("provider_submission_id IS NULL");
        builder.HasIndex(submission => new
        { submission.TenantId, submission.RegistrationProviderBindingId, submission.ProviderSubmissionId, submission.ProviderResponseRevision })
            .IsUnique().HasFilter("provider_submission_id IS NOT NULL");
        builder.HasIndex(submission => new { submission.TenantId, submission.RegistrationAttemptId, submission.ReceivedAt });
        builder.HasIndex(submission => submission.HttpIdempotencyKeyHash);
    }
}
