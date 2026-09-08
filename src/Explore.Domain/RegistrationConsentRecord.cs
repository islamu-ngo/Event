using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationConsentRecord : ITenantEntity, IAuditableEntity
{
    private RegistrationConsentRecord()
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
    public int RegistrationFormVersion { get; private set; }
    public Guid RegistrationFormSectionId { get; private set; }
    public Guid RegistrationFormFieldId { get; private set; }
    public int FieldTypeId { get; private set; }
    public int RequirementSubjectTypeId { get; private set; }
    public Guid? RequirementSubjectId { get; private set; }
    public Guid RequirementSubjectKey { get; private set; }
    public string PurposeCode { get; private set; } = string.Empty;
    public string ConsentTextSnapshot { get; private set; } = string.Empty;
    public string ConsentTextVersion { get; private set; } = string.Empty;
    public string LanguageTag { get; private set; } = string.Empty;
    public int AnswerSubjectTypeId { get; private set; }
    public RegistrationAnswerSubjectType? AnswerSubjectType { get; private set; }
    public Guid? OrderSubjectId { get; private set; }
    public Guid? PurchaserSubjectId { get; private set; }
    public Guid? ParticipantSubjectId { get; private set; }
    public Guid? TicketAssignmentSubjectId { get; private set; }
    public Guid? TicketAssignmentOrderLineId { get; private set; }
    public Guid? SessionSelectionSubjectId { get; private set; }
    public Guid EffectiveSubjectIdentity { get; private set; }
    public DateTime GrantedAt { get; private set; }
    public DateTime? WithdrawnAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static RegistrationConsentRecord Grant(
        RegistrationSubmission submission,
        RegistrationRequirement requirement,
        RegistrationFormVersion version,
        RegistrationFormField field,
        RegistrationAnswerSubjectTypeEnum subjectType,
        Guid subjectId,
        Guid? ticketAssignmentOrderLineId,
        DateTime grantedAt)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(field);
        if (subjectId == Guid.Empty || grantedAt == default || grantedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Consent subject identity and UTC grant time are required.");
        }

        if ((RegistrationFieldTypeEnum)field.FieldTypeId != RegistrationFieldTypeEnum.Consent ||
            !field.RequiresExplicitConsent || field.ConsentPurposeCode is null || field.ConsentText is null ||
            field.ConsentTextVersion is null)
        {
            throw new ArgumentException("Consent evidence requires a consent field with complete metadata.", nameof(field));
        }

        if (version.TenantId != submission.TenantId || version.EventId != submission.EventId ||
            version.RegistrationFormId != submission.RegistrationFormId || version.Id != submission.RegistrationFormVersionId ||
            field.TenantId != submission.TenantId || field.EventId != submission.EventId ||
            field.RegistrationFormId != submission.RegistrationFormId || field.RegistrationFormVersionId != version.Id ||
            requirement.TenantId != submission.TenantId || requirement.EventId != submission.EventId ||
            requirement.RegistrationWorkflowId != submission.RegistrationWorkflowId || requirement.Id != submission.RegistrationRequirementId)
        {
            throw new ArgumentException("Consent evidence must match the pinned submission lineage.");
        }

        RegistrationConsentRecord record = new()
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
            RegistrationFormVersionId = version.Id,
            RegistrationFormVersion = version.Version,
            RegistrationFormSectionId = field.RegistrationFormSectionId,
            RegistrationFormFieldId = field.Id,
            FieldTypeId = field.FieldTypeId,
            RequirementSubjectTypeId = requirement.AppliesToSubjectTypeId,
            RequirementSubjectId = requirement.AppliesToSubjectId,
            PurposeCode = field.ConsentPurposeCode,
            ConsentTextSnapshot = field.ConsentText,
            ConsentTextVersion = field.ConsentTextVersion,
            LanguageTag = version.LanguageTag,
            AnswerSubjectTypeId = (int)subjectType,
            GrantedAt = grantedAt,
            CreatedAt = grantedAt
        };
        record.SetSubject(subjectType, subjectId, ticketAssignmentOrderLineId);
        return record;
    }

    public void Withdraw(DateTime withdrawnAt)
    {
        if (withdrawnAt == default || withdrawnAt.Kind != DateTimeKind.Utc || withdrawnAt < GrantedAt)
        {
            throw new ArgumentException("Withdrawal must be a UTC time on or after the grant.", nameof(withdrawnAt));
        }

        if (WithdrawnAt is not null)
        {
            throw new InvalidOperationException("Consent evidence has already been withdrawn.");
        }

        WithdrawnAt = withdrawnAt;
        UpdatedAt = withdrawnAt;
    }

    private void SetSubject(
        RegistrationAnswerSubjectTypeEnum subjectType,
        Guid subjectId,
        Guid? ticketAssignmentOrderLineId)
    {
        bool permitted = (RegistrationRequirementSubjectTypeEnum)RequirementSubjectTypeId switch
        {
            RegistrationRequirementSubjectTypeEnum.AllOrders =>
                subjectType is RegistrationAnswerSubjectTypeEnum.RegistrationOrder or RegistrationAnswerSubjectTypeEnum.Purchaser,
            RegistrationRequirementSubjectTypeEnum.SpecificTicketType => subjectType == RegistrationAnswerSubjectTypeEnum.TicketAssignment,
            RegistrationRequirementSubjectTypeEnum.EveryParticipant or RegistrationRequirementSubjectTypeEnum.ChildParticipants =>
                subjectType == RegistrationAnswerSubjectTypeEnum.Participant,
            RegistrationRequirementSubjectTypeEnum.LeadBookerOnly => subjectType == RegistrationAnswerSubjectTypeEnum.Purchaser,
            RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection =>
                subjectType == RegistrationAnswerSubjectTypeEnum.SessionSelection && RequirementSubjectId == subjectId,
            _ => false
        };
        bool invalidTicketLine = subjectType == RegistrationAnswerSubjectTypeEnum.TicketAssignment
            ? ticketAssignmentOrderLineId is null || ticketAssignmentOrderLineId == Guid.Empty
            : ticketAssignmentOrderLineId is not null;
        if (!permitted || subjectType is RegistrationAnswerSubjectTypeEnum.RegistrationOrder or RegistrationAnswerSubjectTypeEnum.Purchaser &&
            subjectId != RegistrationOrderId || invalidTicketLine)
        {
            throw new ArgumentException("Consent subject is not permitted by the pinned requirement applicability.", nameof(subjectType));
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
}
