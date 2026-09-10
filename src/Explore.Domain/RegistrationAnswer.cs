using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationAnswer : ITenantEntity, IAuditableEntity, ISoftDeletable
{
    private RegistrationAnswer()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid RegistrationAttemptId { get; private set; }
    public Guid RegistrationSubmissionId { get; private set; }
    public Guid RegistrationWorkflowId { get; private set; }
    public Guid RegistrationRequirementId { get; private set; }
    public Guid RegistrationFormId { get; private set; }
    public Guid RegistrationFormVersionId { get; private set; }
    public Guid RegistrationFormSectionId { get; private set; }
    public Guid RegistrationFormFieldId { get; private set; }
    public int FieldTypeId { get; private set; }
    public int RequirementSubjectTypeId { get; private set; }
    public Guid? RequirementSubjectId { get; private set; }
    public Guid RequirementSubjectKey { get; private set; }
    public int AnswerSubjectTypeId { get; private set; }
    public RegistrationAnswerSubjectType? AnswerSubjectType { get; private set; }
    public Guid? OrderSubjectId { get; private set; }
    public Guid? PurchaserSubjectId { get; private set; }
    public Guid? ParticipantSubjectId { get; private set; }
    public Guid? TicketAssignmentSubjectId { get; private set; }
    public Guid? TicketAssignmentOrderLineId { get; private set; }
    public Guid? SessionSelectionSubjectId { get; private set; }
    public Guid EffectiveSubjectIdentity { get; private set; }
    public int Ordinal { get; private set; }
    public string? TextValue { get; private set; }
    public long? IntegerValue { get; private set; }
    public decimal? DecimalValue { get; private set; }
    public bool? BooleanValue { get; private set; }
    public DateOnly? DateValue { get; private set; }
    public TimeOnly? TimeValue { get; private set; }
    public DateTime? InstantValue { get; private set; }
    public Guid? SelectedOptionId { get; private set; }
    public Guid? SensitiveAnswerValueId { get; private set; }
    public RegistrationSensitiveAnswerValue? SensitiveAnswerValue { get; private set; }
    public DateTime? RetentionUntil { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public static RegistrationAnswer CreateText(
        RegistrationSubmission submission, RegistrationFormField field, RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType, Guid subjectId, int ordinal, string value, DateTime createdAt,
        Guid? ticketAssignmentOrderLineId = null, DateTime? anonymousUpperBoundUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Create(submission, field, requirement, subjectType, subjectId, ordinal, createdAt,
            ticketAssignmentOrderLineId, anonymousUpperBoundUtc, AnswerValueFamily.Text, answer => answer.TextValue = value);
    }

    public static RegistrationAnswer CreateInteger(
        RegistrationSubmission submission, RegistrationFormField field, RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType, Guid subjectId, int ordinal, long value, DateTime createdAt,
        Guid? ticketAssignmentOrderLineId = null, DateTime? anonymousUpperBoundUtc = null) =>
        Create(submission, field, requirement, subjectType, subjectId, ordinal, createdAt,
            ticketAssignmentOrderLineId, anonymousUpperBoundUtc, AnswerValueFamily.Integer, answer => answer.IntegerValue = value);

    public static RegistrationAnswer CreateDecimal(
        RegistrationSubmission submission, RegistrationFormField field, RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType, Guid subjectId, int ordinal, decimal value, DateTime createdAt,
        Guid? ticketAssignmentOrderLineId = null, DateTime? anonymousUpperBoundUtc = null) =>
        Create(submission, field, requirement, subjectType, subjectId, ordinal, createdAt,
            ticketAssignmentOrderLineId, anonymousUpperBoundUtc, AnswerValueFamily.Decimal, answer => answer.DecimalValue = value);

    public static RegistrationAnswer CreateBoolean(
        RegistrationSubmission submission, RegistrationFormField field, RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType, Guid subjectId, int ordinal, bool value, DateTime createdAt,
        Guid? ticketAssignmentOrderLineId = null, DateTime? anonymousUpperBoundUtc = null) =>
        Create(submission, field, requirement, subjectType, subjectId, ordinal, createdAt,
            ticketAssignmentOrderLineId, anonymousUpperBoundUtc, AnswerValueFamily.Boolean, answer => answer.BooleanValue = value);

    public static RegistrationAnswer CreateDate(
        RegistrationSubmission submission, RegistrationFormField field, RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType, Guid subjectId, int ordinal, DateOnly value, DateTime createdAt,
        Guid? ticketAssignmentOrderLineId = null, DateTime? anonymousUpperBoundUtc = null) =>
        Create(submission, field, requirement, subjectType, subjectId, ordinal, createdAt,
            ticketAssignmentOrderLineId, anonymousUpperBoundUtc, AnswerValueFamily.Date, answer => answer.DateValue = value);

    public static RegistrationAnswer CreateTime(
        RegistrationSubmission submission, RegistrationFormField field, RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType, Guid subjectId, int ordinal, TimeOnly value, DateTime createdAt,
        Guid? ticketAssignmentOrderLineId = null, DateTime? anonymousUpperBoundUtc = null) =>
        Create(submission, field, requirement, subjectType, subjectId, ordinal, createdAt,
            ticketAssignmentOrderLineId, anonymousUpperBoundUtc, AnswerValueFamily.Time, answer => answer.TimeValue = value);

    public static RegistrationAnswer CreateInstant(
        RegistrationSubmission submission, RegistrationFormField field, RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType, Guid subjectId, int ordinal, DateTime value, DateTime createdAt,
        Guid? ticketAssignmentOrderLineId = null, DateTime? anonymousUpperBoundUtc = null)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Instant answers require a non-default UTC value.", nameof(value));
        }

        return Create(submission, field, requirement, subjectType, subjectId, ordinal, createdAt,
            ticketAssignmentOrderLineId, anonymousUpperBoundUtc, AnswerValueFamily.Instant, answer => answer.InstantValue = value);
    }

    public static RegistrationAnswer CreateOption(
        RegistrationSubmission submission, RegistrationFormField field, RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType, Guid subjectId, int ordinal,
        RegistrationFormFieldOption option, DateTime createdAt, Guid? ticketAssignmentOrderLineId = null, DateTime? anonymousUpperBoundUtc = null)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (option.TenantId != field.TenantId || option.EventId != field.EventId ||
            option.RegistrationFormId != field.RegistrationFormId ||
            option.RegistrationFormVersionId != field.RegistrationFormVersionId ||
            option.RegistrationFormSectionId != field.RegistrationFormSectionId ||
            option.RegistrationFormFieldId != field.Id)
        {
            throw new ArgumentException("Selected option must belong to the exact pinned field version.", nameof(option));
        }

        return Create(submission, field, requirement, subjectType, subjectId, ordinal, createdAt,
            ticketAssignmentOrderLineId, anonymousUpperBoundUtc, AnswerValueFamily.Option, answer => answer.SelectedOptionId = option.Id);
    }

    public static RegistrationAnswer CreateSensitive(
        RegistrationSubmission submission, RegistrationFormField field, RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType, Guid subjectId, int ordinal,
        RegistrationSensitiveAnswerValue sensitiveValue, DateTime createdAt, Guid? ticketAssignmentOrderLineId = null, DateTime? anonymousUpperBoundUtc = null)
    {
        ArgumentNullException.ThrowIfNull(sensitiveValue);
        if (sensitiveValue.TenantId != submission.TenantId)
        {
            throw new ArgumentException("Sensitive ciphertext must belong to the submission tenant.", nameof(sensitiveValue));
        }

        return Create(submission, field, requirement, subjectType, subjectId, ordinal, createdAt,
            ticketAssignmentOrderLineId, anonymousUpperBoundUtc, AnswerValueFamily.Sensitive, answer =>
            {
                answer.SensitiveAnswerValueId = sensitiveValue.Id;
                answer.SensitiveAnswerValue = sensitiveValue;
            });
    }

    private static RegistrationAnswer Create(
        RegistrationSubmission submission, RegistrationFormField field, RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType, Guid subjectId, int ordinal, DateTime createdAt,
        Guid? ticketAssignmentOrderLineId, DateTime? anonymousUpperBoundUtc, AnswerValueFamily valueFamily, Action<RegistrationAnswer> setValue)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(requirement);
        if (subjectId == Guid.Empty || ordinal <= 0 || createdAt == default || createdAt.Kind != DateTimeKind.Utc)
        {
            throw ordinal <= 0
                ? new ArgumentOutOfRangeException(nameof(ordinal))
                : new ArgumentException("Answer subject identity and UTC creation time are required.");
        }

        ValidateLineage(submission, field, requirement);
        ValidateValueFamily((RegistrationFieldTypeEnum)field.FieldTypeId, valueFamily);
        RegistrationAnswer answer = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = submission.TenantId,
            EventId = submission.EventId,
            RegistrationOrderId = submission.RegistrationOrderId,
            RegistrationAttemptId = submission.RegistrationAttemptId,
            RegistrationSubmissionId = submission.Id,
            RegistrationWorkflowId = submission.RegistrationWorkflowId,
            RegistrationRequirementId = submission.RegistrationRequirementId,
            RegistrationFormId = submission.RegistrationFormId,
            RegistrationFormVersionId = submission.RegistrationFormVersionId,
            RegistrationFormSectionId = field.RegistrationFormSectionId,
            RegistrationFormFieldId = field.Id,
            FieldTypeId = field.FieldTypeId,
            RetentionUntil = RegistrationRetentionDeadline.Resolve(field.RetentionPolicyId, createdAt, anonymousUpperBoundUtc),
            RequirementSubjectTypeId = requirement.AppliesToSubjectTypeId,
            RequirementSubjectId = requirement.AppliesToSubjectId,
            AnswerSubjectTypeId = (int)subjectType,
            Ordinal = ordinal,
            CreatedAt = createdAt
        };
        answer.SetSubject(subjectType, subjectId, ticketAssignmentOrderLineId);
        setValue(answer);
        return answer;
    }

    private static void ValidateLineage(
        RegistrationSubmission submission, RegistrationFormField field, RegistrationRequirement requirement)
    {
        if (field.TenantId != submission.TenantId || field.EventId != submission.EventId ||
            field.RegistrationFormId != submission.RegistrationFormId ||
            field.RegistrationFormVersionId != submission.RegistrationFormVersionId ||
            requirement.TenantId != submission.TenantId || requirement.EventId != submission.EventId ||
            requirement.RegistrationWorkflowId != submission.RegistrationWorkflowId ||
            requirement.Id != submission.RegistrationRequirementId)
        {
            throw new ArgumentException("Answer field and requirement must match the pinned submission lineage.");
        }
    }

    private void SetSubject(
        RegistrationAnswerSubjectTypeEnum subjectType,
        Guid subjectId,
        Guid? ticketAssignmentOrderLineId)
    {
        RegistrationRequirementSubjectTypeEnum applicability = (RegistrationRequirementSubjectTypeEnum)RequirementSubjectTypeId;
        bool permitted = applicability switch
        {
            RegistrationRequirementSubjectTypeEnum.AllOrders =>
                subjectType is RegistrationAnswerSubjectTypeEnum.RegistrationOrder or RegistrationAnswerSubjectTypeEnum.Purchaser,
            RegistrationRequirementSubjectTypeEnum.SpecificTicketType => subjectType == RegistrationAnswerSubjectTypeEnum.TicketAssignment,
            RegistrationRequirementSubjectTypeEnum.EveryParticipant => subjectType == RegistrationAnswerSubjectTypeEnum.Participant,
            RegistrationRequirementSubjectTypeEnum.LeadBookerOnly => subjectType == RegistrationAnswerSubjectTypeEnum.Purchaser,
            RegistrationRequirementSubjectTypeEnum.ChildParticipants => subjectType == RegistrationAnswerSubjectTypeEnum.Participant,
            RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection =>
                subjectType == RegistrationAnswerSubjectTypeEnum.SessionSelection && RequirementSubjectId == subjectId,
            _ => false
        };
        bool invalidTicketLine = subjectType == RegistrationAnswerSubjectTypeEnum.TicketAssignment
            ? ticketAssignmentOrderLineId is null || ticketAssignmentOrderLineId == Guid.Empty
            : ticketAssignmentOrderLineId.HasValue;
        if (!permitted || subjectType is RegistrationAnswerSubjectTypeEnum.RegistrationOrder or RegistrationAnswerSubjectTypeEnum.Purchaser && subjectId != RegistrationOrderId ||
            invalidTicketLine)
        {
            throw new ArgumentException("Answer subject is not permitted by the pinned requirement applicability.", nameof(subjectType));
        }

        switch (subjectType)
        {
            case RegistrationAnswerSubjectTypeEnum.RegistrationOrder: OrderSubjectId = subjectId; break;
            case RegistrationAnswerSubjectTypeEnum.Purchaser: PurchaserSubjectId = subjectId; break;
            case RegistrationAnswerSubjectTypeEnum.Participant: ParticipantSubjectId = subjectId; break;
            case RegistrationAnswerSubjectTypeEnum.TicketAssignment:
                TicketAssignmentSubjectId = subjectId;
                TicketAssignmentOrderLineId = ticketAssignmentOrderLineId;
                break;
            case RegistrationAnswerSubjectTypeEnum.SessionSelection: SessionSelectionSubjectId = subjectId; break;
            default: throw new ArgumentOutOfRangeException(nameof(subjectType));
        }
    }

    private static void ValidateValueFamily(RegistrationFieldTypeEnum fieldType, AnswerValueFamily family)
    {
        bool valid = family == AnswerValueFamily.Sensitive
            ? fieldType is RegistrationFieldTypeEnum.ShortText or RegistrationFieldTypeEnum.LongText or
                RegistrationFieldTypeEnum.Integer or RegistrationFieldTypeEnum.Decimal or RegistrationFieldTypeEnum.Boolean or
                RegistrationFieldTypeEnum.Date or RegistrationFieldTypeEnum.Time or RegistrationFieldTypeEnum.Instant or
                RegistrationFieldTypeEnum.Email or RegistrationFieldTypeEnum.Phone or RegistrationFieldTypeEnum.Url or
                RegistrationFieldTypeEnum.CountryCode or RegistrationFieldTypeEnum.LanguageTag or RegistrationFieldTypeEnum.Rating
            : fieldType switch
            {
                RegistrationFieldTypeEnum.ShortText or RegistrationFieldTypeEnum.LongText or RegistrationFieldTypeEnum.Email or
                    RegistrationFieldTypeEnum.Phone or RegistrationFieldTypeEnum.Url or RegistrationFieldTypeEnum.CountryCode or
                    RegistrationFieldTypeEnum.LanguageTag => family == AnswerValueFamily.Text,
                RegistrationFieldTypeEnum.Integer or RegistrationFieldTypeEnum.Rating => family == AnswerValueFamily.Integer,
                RegistrationFieldTypeEnum.Decimal => family == AnswerValueFamily.Decimal,
                RegistrationFieldTypeEnum.Boolean => family == AnswerValueFamily.Boolean,
                RegistrationFieldTypeEnum.Date => family == AnswerValueFamily.Date,
                RegistrationFieldTypeEnum.Time => family == AnswerValueFamily.Time,
                RegistrationFieldTypeEnum.Instant => family == AnswerValueFamily.Instant,
                RegistrationFieldTypeEnum.SingleChoice or RegistrationFieldTypeEnum.MultipleChoice => family == AnswerValueFamily.Option,
                _ => false
            };
        if (!valid)
        {
            throw new ArgumentException("Answer value family does not match the declared field type.");
        }
    }

    private enum AnswerValueFamily { Text, Integer, Decimal, Boolean, Date, Time, Instant, Option, Sensitive }
}
