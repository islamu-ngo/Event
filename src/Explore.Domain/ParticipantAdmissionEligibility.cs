using Explore.Domain.Interfaces;

namespace Explore.Domain;

public enum ParticipantAdmissionReadinessCode
{
    Ready = 1,
    OrderNotConfirmed = 2,
    PaymentPending = 3,
    SubjectOwnershipPending = 4,
    ParticipantCompletionPending = 5,
    SubjectConsentPending = 6,
    ApprovalPending = 7,
    Revoked = 8,
}

public sealed record ParticipantAdmissionReadinessFacts(
    bool OrderConfirmed,
    bool PaymentSatisfied,
    bool RequirementsComplete,
    bool SubjectOwnershipEstablished,
    bool ConsentRequired,
    bool SubjectConsentActive,
    bool ApprovalRequired,
    bool ApprovalGranted,
    bool Revoked);

public sealed record ParticipantAdmissionReadinessDecision(
    ParticipantAdmissionReadinessCode Code)
{
    public bool IsReady =>
        Code == ParticipantAdmissionReadinessCode.Ready;
}

public static class ParticipantAdmissionReadinessRules
{
    public static ParticipantAdmissionReadinessDecision Decide(
        ParticipantAdmissionReadinessFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ParticipantAdmissionReadinessCode code =
            facts switch
            {
                { Revoked: true } =>
                    ParticipantAdmissionReadinessCode.Revoked,
                { OrderConfirmed: false } =>
                    ParticipantAdmissionReadinessCode
                        .OrderNotConfirmed,
                { PaymentSatisfied: false } =>
                    ParticipantAdmissionReadinessCode
                        .PaymentPending,
                { SubjectOwnershipEstablished: false } =>
                    ParticipantAdmissionReadinessCode
                        .SubjectOwnershipPending,
                { RequirementsComplete: false } =>
                    ParticipantAdmissionReadinessCode
                        .ParticipantCompletionPending,
                {
                    ConsentRequired: true,
                    SubjectConsentActive: false,
                } =>
                    ParticipantAdmissionReadinessCode
                        .SubjectConsentPending,
                {
                    ApprovalRequired: true,
                    ApprovalGranted: false,
                } =>
                    ParticipantAdmissionReadinessCode
                        .ApprovalPending,
                _ => ParticipantAdmissionReadinessCode.Ready,
            };
        return new ParticipantAdmissionReadinessDecision(code);
    }
}

public sealed class ParticipantAdmissionEligibility :
    ITenantEntity,
    IAuditableEntity,
    IConcurrencyAware
{
    private Guid _tenantId;

    private ParticipantAdmissionEligibility()
    {
    }

    private ParticipantAdmissionEligibility(
        Guid tenantId,
        Guid eventId,
        RegistrationTicketAssignment assignment,
        RegistrationParticipant participant,
        bool consentRequired,
        bool approvalRequired,
        DateTime createdAt)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        EventId = eventId;
        RegistrationOrderId = assignment.RegistrationOrderId;
        RegistrationOrderLineId =
            assignment.RegistrationOrderLineId;
        RegistrationTicketAssignmentId = assignment.Id;
        ParticipantId = participant.Id;
        ConsentRequired = consentRequired;
        ApprovalRequired = approvalRequired;
        CreatedAt = createdAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(
            ref _tenantId,
            value,
            nameof(ParticipantAdmissionEligibility));
    }

    public Guid EventId { get; private set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid RegistrationOrderLineId { get; private set; }
    public Guid RegistrationTicketAssignmentId
    {
        get;
        private set;
    }
    public RegistrationTicketAssignment? RegistrationTicketAssignment
    {
        get;
        private set;
    }
    public Guid ParticipantId { get; private set; }
    public RegistrationParticipant? Participant
    {
        get;
        private set;
    }
    public Guid? SubjectUserId { get; private set; }
    public DateTime? RequirementsCompletedAt { get; private set; }
    public Guid? SubjectConsentRecordId { get; private set; }
    public RegistrationConsentRecord? SubjectConsentRecord
    {
        get;
        private set;
    }
    public DateTime? SubjectConsentGrantedAt { get; private set; }
    public bool ConsentRequired { get; private set; }
    public bool ApprovalRequired { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public Guid? ApprovedByActorId { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public Guid? RevokedByActorId { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static ParticipantAdmissionEligibility Create(
        Guid tenantId,
        Guid eventId,
        RegistrationTicketAssignment assignment,
        RegistrationParticipant participant,
        bool consentRequired,
        bool approvalRequired,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(participant);
        DateTime created = EnsureUtc(
            createdAt,
            nameof(createdAt));
        if (tenantId == Guid.Empty
            || eventId == Guid.Empty
            || assignment.TenantId != tenantId
            || participant.TenantId != tenantId
            || assignment.RegistrationOrderId !=
            participant.RegistrationOrderId
            || assignment.ParticipantId != participant.Id)
        {
            throw new ArgumentException(
                "Eligibility must match one assigned participant.");
        }

        return new ParticipantAdmissionEligibility(
            tenantId,
            eventId,
            assignment,
            participant,
            consentRequired,
            approvalRequired,
            created);
    }

    public void RecordSubjectCompletion(
        RegistrationParticipant participant,
        Guid subjectUserId,
        Guid? subjectConsentRecordId,
        DateTime completedAt,
        Guid concurrencyStamp)
    {
        ArgumentNullException.ThrowIfNull(participant);
        DateTime completed = EnsureUtc(
            completedAt,
            nameof(completedAt));
        if (participant.Id != ParticipantId
            || participant.TenantId != TenantId
            || participant.RegistrationOrderId !=
            RegistrationOrderId
            || participant.LinkedUserId != subjectUserId
            || subjectUserId == Guid.Empty
            || subjectConsentRecordId == Guid.Empty
            || concurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException(
                "Completion must be recorded by the linked subject.");
        }

        SubjectUserId = subjectUserId;
        RequirementsCompletedAt = completed;
        SubjectConsentRecordId = subjectConsentRecordId;
        SubjectConsentGrantedAt =
            subjectConsentRecordId.HasValue ? completed : null;
        UpdatedAt = completed;
        ConcurrencyStamp = concurrencyStamp;
    }

    public void Approve(
        Guid actorId,
        DateTime approvedAt,
        Guid concurrencyStamp)
    {
        DateTime approved = EnsureUtc(
            approvedAt,
            nameof(approvedAt));
        if (actorId == Guid.Empty
            || concurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException(
                "Approval actor and concurrency are required.");
        }
        if (RevokedAt.HasValue)
        {
            throw new InvalidOperationException(
                "Revoked participant admission cannot be approved.");
        }

        ApprovedAt = approved;
        ApprovedByActorId = actorId;
        UpdatedAt = approved;
        ConcurrencyStamp = concurrencyStamp;
    }

    public void Revoke(
        Guid actorId,
        DateTime revokedAt,
        Guid concurrencyStamp)
    {
        DateTime revoked = EnsureUtc(
            revokedAt,
            nameof(revokedAt));
        if (actorId == Guid.Empty
            || concurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException(
                "Revocation actor and concurrency are required.");
        }
        if (RevokedAt.HasValue)
        {
            return;
        }

        RevokedAt = revoked;
        RevokedByActorId = actorId;
        UpdatedAt = revoked;
        ConcurrencyStamp = concurrencyStamp;
    }

    public void TransferTo(
        RegistrationParticipant recipient,
        Guid recipientSubjectUserId,
        Guid? subjectConsentRecordId,
        bool requirementsComplete,
        Guid? approvedByActorId,
        DateTime completedAt,
        Guid concurrencyStamp)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        DateTime completed = EnsureUtc(
            completedAt,
            nameof(completedAt));
        if (RevokedAt.HasValue
            || recipient.TenantId != TenantId
            || recipient.RegistrationOrderId !=
            RegistrationOrderId
            || recipient.LinkedUserId !=
            recipientSubjectUserId
            || recipientSubjectUserId == Guid.Empty
            || recipient.Id == ParticipantId
            || !requirementsComplete
            || ConsentRequired
            && !subjectConsentRecordId.HasValue
            || ApprovalRequired
            && !approvedByActorId.HasValue
            || concurrencyStamp == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Recipient readiness does not permit transfer acceptance.");
        }

        ParticipantId = recipient.Id;
        SubjectUserId = recipientSubjectUserId;
        RequirementsCompletedAt = completed;
        SubjectConsentRecordId = subjectConsentRecordId;
        SubjectConsentGrantedAt =
            subjectConsentRecordId.HasValue
                ? completed
                : null;
        ApprovedAt = approvedByActorId.HasValue
            ? completed
            : null;
        ApprovedByActorId = approvedByActorId;
        UpdatedAt = completed;
        ConcurrencyStamp = concurrencyStamp;
    }

    public ParticipantAdmissionReadinessDecision DescribeReadiness(
        bool orderConfirmed,
        bool paymentSatisfied) =>
        ParticipantAdmissionReadinessRules.Decide(
            new ParticipantAdmissionReadinessFacts(
                orderConfirmed,
                paymentSatisfied,
                RequirementsCompletedAt.HasValue,
                SubjectUserId.HasValue,
                ConsentRequired,
                !ConsentRequired
                    || SubjectConsentRecordId.HasValue
                    && SubjectConsentGrantedAt.HasValue,
                ApprovalRequired,
                !ApprovalRequired
                    || ApprovedAt.HasValue,
                RevokedAt.HasValue));

    private static DateTime EnsureUtc(
        DateTime value,
        string parameterName)
    {
        if (value == default
            || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Timestamp must be UTC.",
                parameterName);
        }

        return value;
    }
}
