using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ParticipantAdmissionEligibilityConfiguration :
    IEntityTypeConfiguration<ParticipantAdmissionEligibility>
{
    public void Configure(
        EntityTypeBuilder<ParticipantAdmissionEligibility> builder)
    {
        builder.ToTable(
            table =>
            {
                table.HasCheckConstraint(
                    "ck_participant_admission_completion_consent",
                    "(subject_consent_record_id IS NULL AND subject_consent_granted_at IS NULL) OR " +
                    "(subject_consent_record_id IS NOT NULL AND subject_consent_granted_at IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_participant_admission_approval",
                    "(approved_at IS NULL AND approved_by_actor_id IS NULL) OR " +
                    "(approved_at IS NOT NULL AND approved_by_actor_id IS NOT NULL)");
                table.HasCheckConstraint(
                    "ck_participant_admission_revocation",
                    "(revoked_at IS NULL AND revoked_by_actor_id IS NULL) OR " +
                    "(revoked_at IS NOT NULL AND revoked_by_actor_id IS NOT NULL)");
            });
        builder.Property(value => value.Id)
            .ValueGeneratedNever();
        builder.Property(value => value.ConcurrencyStamp)
            .IsConcurrencyToken();
        builder.Property(value => value.CreatedAt)
            .IsRequired();
        builder.HasAlternateKey(value =>
            new { value.TenantId, value.Id });
        builder.HasIndex(value => new
            {
                value.TenantId,
                value.RegistrationTicketAssignmentId,
            })
            .IsUnique();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value =>
                value.RegistrationTicketAssignment)
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.RegistrationOrderId,
                value.RegistrationTicketAssignmentId,
                value.RegistrationOrderLineId,
            })
            .HasPrincipalKey(assignment => new
            {
                assignment.TenantId,
                assignment.RegistrationOrderId,
                assignment.Id,
                assignment.RegistrationOrderLineId,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Participant)
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.RegistrationOrderId,
                value.ParticipantId,
            })
            .HasPrincipalKey(participant => new
            {
                participant.TenantId,
                participant.RegistrationOrderId,
                participant.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.SubjectConsentRecord)
            .WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.SubjectConsentRecordId,
            })
            .HasPrincipalKey(record => new
            {
                record.TenantId,
                record.Id,
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(value => value.SubjectUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new
        {
            value.TenantId,
            value.RegistrationOrderId,
            value.ParticipantId,
        });
        builder.HasIndex(value => new
        {
            value.TenantId,
            value.EventId,
        });
    }
}
