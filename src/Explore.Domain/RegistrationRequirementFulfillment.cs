using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationRequirementFulfillment : ITenantEntity, IAuditableEntity
{
    private RegistrationRequirementFulfillment()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid RegistrationWorkflowId { get; private set; }
    public Guid RegistrationRequirementId { get; private set; }
    public int SubjectTypeId { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid? SourceRegistrationSubmissionId { get; private set; }
    public bool IsSkipped { get; private set; }
    public DateTime RecordedAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static RegistrationRequirementFulfillment CreateFulfilled(
        RegistrationOrder order,
        RegistrationRequirement requirement,
        RegistrationSubmission submission,
        RegistrationAnswerSubjectTypeEnum subjectType,
        Guid subjectId,
        DateTime recordedAt)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ValidateLineage(order, requirement, subjectType, subjectId, recordedAt);
        if (!submission.IsFinalizable || submission.TenantId != order.TenantId || submission.EventId != order.EventId ||
            submission.RegistrationOrderId != order.Id || submission.RegistrationWorkflowId != requirement.RegistrationWorkflowId ||
            submission.RegistrationRequirementId != requirement.Id)
        {
            throw new ArgumentException("Finalizable submission evidence must match the order requirement.", nameof(submission));
        }

        return Create(order, requirement, subjectType, subjectId, submission.Id, false, recordedAt);
    }

    public static RegistrationRequirementFulfillment CreateSkipped(
        RegistrationOrder order,
        RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType,
        Guid subjectId,
        DateTime recordedAt)
    {
        ValidateLineage(order, requirement, subjectType, subjectId, recordedAt);
        if (!requirement.CanSkip || requirement.CriticalityId == (int)RegistrationRequirementCriticalityEnum.Required)
        {
            throw new InvalidOperationException("REGISTRATION_REQUIREMENT_SKIP_FORBIDDEN");
        }

        return Create(order, requirement, subjectType, subjectId, null, true, recordedAt);
    }

    private static RegistrationRequirementFulfillment Create(
        RegistrationOrder order,
        RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType,
        Guid subjectId,
        Guid? submissionId,
        bool isSkipped,
        DateTime recordedAt) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = order.TenantId,
            EventId = order.EventId,
            RegistrationOrderId = order.Id,
            RegistrationWorkflowId = requirement.RegistrationWorkflowId,
            RegistrationRequirementId = requirement.Id,
            SubjectTypeId = (int)subjectType,
            SubjectId = subjectId,
            SourceRegistrationSubmissionId = submissionId,
            IsSkipped = isSkipped,
            RecordedAt = recordedAt,
            CreatedAt = recordedAt
        };

    private static void ValidateLineage(
        RegistrationOrder order,
        RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType,
        Guid subjectId,
        DateTime recordedAt)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(requirement);
        if (!Enum.IsDefined(subjectType) || subjectId == Guid.Empty || recordedAt == default || recordedAt.Kind != DateTimeKind.Utc ||
            order.RegistrationWorkflowVersionId != requirement.RegistrationWorkflowId ||
            order.TenantId != requirement.TenantId || order.EventId != requirement.EventId)
        {
            throw new ArgumentException("Fulfillment must match the pinned order workflow and subject.");
        }
    }
}
