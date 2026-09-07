using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationRequirementFulfillmentConfiguration : IEntityTypeConfiguration<RegistrationRequirementFulfillment>
{
    public void Configure(EntityTypeBuilder<RegistrationRequirementFulfillment> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_registration_requirement_fulfillments_outcome",
            "(is_skipped = true AND source_registration_submission_id IS NULL) OR " +
            "(is_skipped = false AND source_registration_submission_id IS NOT NULL)"));
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.RecordedAt).IsRequired();
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrder>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.EventId, value.RegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.EventId, order.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationRequirement>().WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.EventId,
                value.RegistrationWorkflowId,
                value.RegistrationRequirementId
            })
            .HasPrincipalKey(requirement => new
            {
                requirement.TenantId,
                requirement.EventId,
                requirement.RegistrationWorkflowId,
                requirement.Id
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationSubmission>().WithMany()
            .HasForeignKey(value => new { value.TenantId, value.EventId, value.SourceRegistrationSubmissionId })
            .HasPrincipalKey(submission => new { submission.TenantId, submission.EventId, submission.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationAnswerSubjectType>().WithMany()
            .HasForeignKey(value => value.SubjectTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new
        {
            value.TenantId,
            value.RegistrationOrderId,
            value.RegistrationRequirementId,
            value.SubjectTypeId,
            value.SubjectId,
            value.IsSkipped
        }).IsUnique();
    }
}
