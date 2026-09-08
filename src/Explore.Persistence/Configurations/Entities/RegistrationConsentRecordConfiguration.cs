using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationConsentRecordConfiguration : IEntityTypeConfiguration<RegistrationConsentRecord>
{
    private const string ZeroUuid = "'00000000-0000-0000-0000-000000000000'::uuid";

    public void Configure(EntityTypeBuilder<RegistrationConsentRecord> builder)
    {
        builder.HasAlternateKey(record => new
        {
            record.TenantId,
            record.Id,
        });
        builder.ToTable(table =>
            table.HasCheckConstraint("ck_registration_consent_records_subject_shape", SubjectConstraint()));
        builder.Property(record => record.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(record => record.PurposeCode).HasMaxLength(100).IsRequired();
        builder.Property(record => record.ConsentTextSnapshot).HasMaxLength(4000).IsRequired();
        builder.Property(record => record.ConsentTextVersion).HasMaxLength(100).IsRequired();
        builder.Property(record => record.LanguageTag).HasMaxLength(35).IsRequired();
        ConfigureComputedUuid(builder.Property(record => record.RequirementSubjectKey),
            $"COALESCE(requirement_subject_id, {ZeroUuid})");
        ConfigureComputedUuid(builder.Property(record => record.EffectiveSubjectIdentity),
            "COALESCE(order_subject_id, purchaser_subject_id, participant_subject_id, ticket_assignment_subject_id, session_selection_subject_id)");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(record => record.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany().HasForeignKey(record => new { record.TenantId, record.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationSubmission>().WithMany()
            .HasForeignKey(record => new
            {
                record.TenantId,
                record.EventId,
                record.RegistrationOrderId,
                record.RegistrationWorkflowId,
                record.RegistrationRequirementId,
                record.RegistrationFormId,
                record.RegistrationFormVersionId,
                record.RegistrationAttemptId,
                record.RegistrationSubmissionId
            })
            .HasPrincipalKey(submission => new
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
            }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationRequirement>().WithMany()
            .HasForeignKey(record => new
            {
                record.TenantId,
                record.EventId,
                record.RegistrationWorkflowId,
                record.RegistrationRequirementId,
                record.RequirementSubjectTypeId,
                record.RequirementSubjectKey
            })
            .HasPrincipalKey(requirement => new
            {
                requirement.TenantId,
                requirement.EventId,
                requirement.RegistrationWorkflowId,
                requirement.Id,
                requirement.AppliesToSubjectTypeId,
                requirement.AppliesToSubjectKey
            }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationFormField>().WithMany()
            .HasForeignKey(record => new
            {
                record.TenantId,
                record.EventId,
                record.RegistrationFormId,
                record.RegistrationFormVersionId,
                record.RegistrationFormSectionId,
                record.RegistrationFormFieldId,
                record.FieldTypeId
            })
            .HasPrincipalKey(field => new
            {
                field.TenantId,
                field.EventId,
                field.RegistrationFormId,
                field.RegistrationFormVersionId,
                field.RegistrationFormSectionId,
                field.Id,
                field.FieldTypeId
            }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationParticipant>().WithMany()
            .HasForeignKey(record => new { record.TenantId, record.RegistrationOrderId, Id = record.ParticipantSubjectId })
            .HasPrincipalKey(participant => new { participant.TenantId, participant.RegistrationOrderId, participant.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationTicketAssignment>().WithMany()
            .HasForeignKey(record => new
            {
                record.TenantId,
                record.RegistrationOrderId,
                Id = record.TicketAssignmentSubjectId,
                record.TicketAssignmentOrderLineId
            })
            .HasPrincipalKey(assignment => new
            {
                assignment.TenantId,
                assignment.RegistrationOrderId,
                assignment.Id,
                assignment.RegistrationOrderLineId
            }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrderLine>().WithMany()
            .HasForeignKey(record => new
            {
                record.TenantId,
                record.RegistrationOrderId,
                Id = record.TicketAssignmentOrderLineId,
                TicketTypeId = record.RequirementSubjectId
            })
            .HasPrincipalKey(line => new
            {
                line.TenantId,
                line.RegistrationOrderId,
                line.Id,
                line.TicketTypeId
            }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(record => record.AnswerSubjectType).WithMany()
            .HasForeignKey(record => record.AnswerSubjectTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(record => new
        {
            record.TenantId,
            record.RegistrationSubmissionId,
            record.RegistrationFormFieldId,
            record.AnswerSubjectTypeId,
            record.EffectiveSubjectIdentity
        }).IsUnique();
        builder.HasIndex(record => new
        {
            record.TenantId,
            record.AnswerSubjectTypeId,
            record.EffectiveSubjectIdentity,
            record.WithdrawnAt
        });
    }

    private static void ConfigureComputedUuid(PropertyBuilder<Guid> property, string sql)
    {
        property.HasColumnType("uuid").HasComputedColumnSql(sql, stored: true).ValueGeneratedOnAdd().IsRequired();
        property.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    private static string SubjectConstraint() =>
        "num_nonnulls(order_subject_id, purchaser_subject_id, participant_subject_id, ticket_assignment_subject_id, session_selection_subject_id) = 1 AND (" +
        $"(answer_subject_type_id = {(int)RegistrationAnswerSubjectTypeEnum.RegistrationOrder} AND order_subject_id = registration_order_id AND ticket_assignment_order_line_id IS NULL AND requirement_subject_type_id = {(int)RegistrationRequirementSubjectTypeEnum.AllOrders}) OR " +
        $"(answer_subject_type_id = {(int)RegistrationAnswerSubjectTypeEnum.Purchaser} AND purchaser_subject_id = registration_order_id AND ticket_assignment_order_line_id IS NULL AND requirement_subject_type_id IN ({(int)RegistrationRequirementSubjectTypeEnum.AllOrders}, {(int)RegistrationRequirementSubjectTypeEnum.LeadBookerOnly})) OR " +
        $"(answer_subject_type_id = {(int)RegistrationAnswerSubjectTypeEnum.Participant} AND participant_subject_id IS NOT NULL AND ticket_assignment_order_line_id IS NULL AND requirement_subject_type_id IN ({(int)RegistrationRequirementSubjectTypeEnum.EveryParticipant}, {(int)RegistrationRequirementSubjectTypeEnum.ChildParticipants})) OR " +
        $"(answer_subject_type_id = {(int)RegistrationAnswerSubjectTypeEnum.TicketAssignment} AND ticket_assignment_subject_id IS NOT NULL AND ticket_assignment_order_line_id IS NOT NULL AND requirement_subject_id IS NOT NULL AND requirement_subject_type_id = {(int)RegistrationRequirementSubjectTypeEnum.SpecificTicketType}) OR " +
        $"(answer_subject_type_id = {(int)RegistrationAnswerSubjectTypeEnum.SessionSelection} AND session_selection_subject_id = requirement_subject_id AND ticket_assignment_order_line_id IS NULL AND requirement_subject_type_id = {(int)RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection}))";
}
