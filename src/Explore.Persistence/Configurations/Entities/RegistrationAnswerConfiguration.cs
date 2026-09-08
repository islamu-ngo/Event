using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationAnswerConfiguration : IEntityTypeConfiguration<RegistrationAnswer>
{
    private const string ZeroUuid = "'00000000-0000-0000-0000-000000000000'::uuid";

    public void Configure(EntityTypeBuilder<RegistrationAnswer> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_registration_answers_exactly_one_value",
                "(CASE WHEN text_value IS NULL THEN 0 ELSE 1 END + " +
                "CASE WHEN integer_value IS NULL THEN 0 ELSE 1 END + " +
                "CASE WHEN decimal_value IS NULL THEN 0 ELSE 1 END + " +
                "CASE WHEN boolean_value IS NULL THEN 0 ELSE 1 END + " +
                "CASE WHEN date_value IS NULL THEN 0 ELSE 1 END + " +
                "CASE WHEN time_value IS NULL THEN 0 ELSE 1 END + " +
                "CASE WHEN instant_value IS NULL THEN 0 ELSE 1 END + " +
                "CASE WHEN selected_option_id IS NULL THEN 0 ELSE 1 END + " +
                "CASE WHEN sensitive_answer_value_id IS NULL THEN 0 ELSE 1 END) = 1");
            table.HasCheckConstraint("ck_registration_answers_value_matches_field_type", ValueTypeConstraint());
            table.HasCheckConstraint("ck_registration_answers_subject_shape", SubjectConstraint());
            table.HasCheckConstraint("ck_registration_answers_positive_ordinal", "ordinal > 0");
        });
        builder.Property(answer => answer.Id).ValueGeneratedNever();
        builder.Property(answer => answer.TextValue).HasMaxLength(10000);
        builder.Property(answer => answer.IntegerValue).HasColumnType("bigint");
        builder.Property(answer => answer.DecimalValue).HasPrecision(19, 4);
        builder.Property(answer => answer.DateValue).HasColumnType("date");
        builder.Property(answer => answer.TimeValue).HasColumnType("time without time zone");
        builder.Property(answer => answer.InstantValue).HasColumnType("timestamp with time zone");
        builder.Property(answer => answer.RetentionUntil).HasColumnType("timestamp with time zone");
        builder.Property(answer => answer.CreatedAt).IsRequired();
        builder.Property(answer => answer.IsDeleted).HasDefaultValue(false);

        ConfigureComputedUuid(builder.Property(answer => answer.RequirementSubjectKey),
            $"COALESCE(requirement_subject_id, {ZeroUuid})");
        ConfigureComputedUuid(builder.Property(answer => answer.EffectiveSubjectIdentity),
            "COALESCE(order_subject_id, purchaser_subject_id, participant_subject_id, ticket_assignment_subject_id, session_selection_subject_id)");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(answer => answer.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany().HasForeignKey(answer => new { answer.TenantId, answer.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationSubmission>().WithMany()
            .HasForeignKey(answer => new
            {
                answer.TenantId,
                answer.EventId,
                answer.RegistrationOrderId,
                answer.RegistrationWorkflowId,
                answer.RegistrationRequirementId,
                answer.RegistrationFormId,
                answer.RegistrationFormVersionId,
                answer.RegistrationAttemptId,
                answer.RegistrationSubmissionId
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
            .HasForeignKey(answer => new
            {
                answer.TenantId,
                answer.EventId,
                answer.RegistrationWorkflowId,
                answer.RegistrationRequirementId,
                answer.RequirementSubjectTypeId,
                answer.RequirementSubjectKey
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
            .HasForeignKey(answer => new
            {
                answer.TenantId,
                answer.EventId,
                answer.RegistrationFormId,
                answer.RegistrationFormVersionId,
                answer.RegistrationFormSectionId,
                answer.RegistrationFormFieldId,
                answer.FieldTypeId
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
        builder.HasOne<RegistrationFormFieldOption>().WithMany()
            .HasForeignKey(answer => new
            {
                answer.TenantId,
                answer.EventId,
                answer.RegistrationFormId,
                answer.RegistrationFormVersionId,
                answer.RegistrationFormSectionId,
                answer.RegistrationFormFieldId,
                Id = answer.SelectedOptionId
            })
            .HasPrincipalKey(option => new
            {
                option.TenantId,
                option.EventId,
                option.RegistrationFormId,
                option.RegistrationFormVersionId,
                option.RegistrationFormSectionId,
                option.RegistrationFormFieldId,
                option.Id
            }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationParticipant>().WithMany()
            .HasForeignKey(answer => new { answer.TenantId, answer.RegistrationOrderId, Id = answer.ParticipantSubjectId })
            .HasPrincipalKey(participant => new { participant.TenantId, participant.RegistrationOrderId, participant.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationTicketAssignment>().WithMany()
            .HasForeignKey(answer => new
            {
                answer.TenantId,
                answer.RegistrationOrderId,
                Id = answer.TicketAssignmentSubjectId,
                RegistrationOrderLineId = answer.TicketAssignmentOrderLineId
            })
            .HasPrincipalKey(assignment => new
            {
                assignment.TenantId,
                assignment.RegistrationOrderId,
                assignment.Id,
                assignment.RegistrationOrderLineId
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationOrderLine>().WithMany()
            .HasForeignKey(answer => new
            {
                answer.TenantId,
                answer.RegistrationOrderId,
                Id = answer.TicketAssignmentOrderLineId,
                TicketTypeId = answer.RequirementSubjectId
            })
            .HasPrincipalKey(line => new
            {
                line.TenantId,
                line.RegistrationOrderId,
                line.Id,
                line.TicketTypeId
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(answer => answer.AnswerSubjectType).WithMany()
            .HasForeignKey(answer => answer.AnswerSubjectTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(answer => answer.SensitiveAnswerValue).WithOne()
            .HasForeignKey<RegistrationAnswer>(answer => new { answer.TenantId, answer.SensitiveAnswerValueId })
            .HasPrincipalKey<RegistrationSensitiveAnswerValue>(value => new { value.TenantId, value.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(answer => new
        {
            answer.TenantId,
            answer.RegistrationSubmissionId,
            answer.RegistrationFormFieldId,
            answer.AnswerSubjectTypeId,
            answer.EffectiveSubjectIdentity,
            answer.Ordinal
        })
            .IsUnique();
        builder.HasIndex(answer => new { answer.TenantId, answer.RegistrationOrderId });
    }

    private static void ConfigureComputedUuid(PropertyBuilder<Guid> property, string sql)
    {
        property.HasColumnType("uuid").HasComputedColumnSql(sql, stored: true).ValueGeneratedOnAdd().IsRequired();
        property.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    private static string ValueTypeConstraint() =>
        $"(field_type_id IN ({(int)RegistrationFieldTypeEnum.ShortText}, {(int)RegistrationFieldTypeEnum.LongText}, {(int)RegistrationFieldTypeEnum.Email}, {(int)RegistrationFieldTypeEnum.Phone}, {(int)RegistrationFieldTypeEnum.Url}, {(int)RegistrationFieldTypeEnum.CountryCode}, {(int)RegistrationFieldTypeEnum.LanguageTag}) AND (text_value IS NOT NULL OR sensitive_answer_value_id IS NOT NULL)) OR " +
        $"(field_type_id IN ({(int)RegistrationFieldTypeEnum.Integer}, {(int)RegistrationFieldTypeEnum.Rating}) AND (integer_value IS NOT NULL OR sensitive_answer_value_id IS NOT NULL)) OR " +
        $"(field_type_id = {(int)RegistrationFieldTypeEnum.Decimal} AND (decimal_value IS NOT NULL OR sensitive_answer_value_id IS NOT NULL)) OR " +
        $"(field_type_id = {(int)RegistrationFieldTypeEnum.Boolean} AND (boolean_value IS NOT NULL OR sensitive_answer_value_id IS NOT NULL)) OR " +
        $"(field_type_id = {(int)RegistrationFieldTypeEnum.Date} AND (date_value IS NOT NULL OR sensitive_answer_value_id IS NOT NULL)) OR " +
        $"(field_type_id = {(int)RegistrationFieldTypeEnum.Time} AND (time_value IS NOT NULL OR sensitive_answer_value_id IS NOT NULL)) OR " +
        $"(field_type_id = {(int)RegistrationFieldTypeEnum.Instant} AND (instant_value IS NOT NULL OR sensitive_answer_value_id IS NOT NULL)) OR " +
        $"(field_type_id IN ({(int)RegistrationFieldTypeEnum.SingleChoice}, {(int)RegistrationFieldTypeEnum.MultipleChoice}) AND selected_option_id IS NOT NULL)";

    private static string SubjectConstraint() =>
        "(CASE WHEN order_subject_id IS NULL THEN 0 ELSE 1 END + " +
        "CASE WHEN purchaser_subject_id IS NULL THEN 0 ELSE 1 END + " +
        "CASE WHEN participant_subject_id IS NULL THEN 0 ELSE 1 END + " +
        "CASE WHEN ticket_assignment_subject_id IS NULL THEN 0 ELSE 1 END + " +
        "CASE WHEN session_selection_subject_id IS NULL THEN 0 ELSE 1 END) = 1 AND (" +
        $"(answer_subject_type_id = {(int)RegistrationAnswerSubjectTypeEnum.RegistrationOrder} AND order_subject_id = registration_order_id AND ticket_assignment_order_line_id IS NULL AND requirement_subject_type_id = {(int)RegistrationRequirementSubjectTypeEnum.AllOrders}) OR " +
        $"(answer_subject_type_id = {(int)RegistrationAnswerSubjectTypeEnum.Purchaser} AND purchaser_subject_id = registration_order_id AND ticket_assignment_order_line_id IS NULL AND requirement_subject_type_id IN ({(int)RegistrationRequirementSubjectTypeEnum.AllOrders}, {(int)RegistrationRequirementSubjectTypeEnum.LeadBookerOnly})) OR " +
        $"(answer_subject_type_id = {(int)RegistrationAnswerSubjectTypeEnum.Participant} AND participant_subject_id IS NOT NULL AND ticket_assignment_order_line_id IS NULL AND requirement_subject_type_id IN ({(int)RegistrationRequirementSubjectTypeEnum.EveryParticipant}, {(int)RegistrationRequirementSubjectTypeEnum.ChildParticipants})) OR " +
        $"(answer_subject_type_id = {(int)RegistrationAnswerSubjectTypeEnum.TicketAssignment} AND ticket_assignment_subject_id IS NOT NULL AND ticket_assignment_order_line_id IS NOT NULL AND requirement_subject_id IS NOT NULL AND requirement_subject_type_id = {(int)RegistrationRequirementSubjectTypeEnum.SpecificTicketType}) OR " +
        $"(answer_subject_type_id = {(int)RegistrationAnswerSubjectTypeEnum.SessionSelection} AND session_selection_subject_id = requirement_subject_id AND ticket_assignment_order_line_id IS NULL AND requirement_subject_type_id = {(int)RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection}))";
}
