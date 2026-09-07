using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationTicketAssignment : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    private Guid _tenantId;

    private RegistrationTicketAssignment()
    {
    }

    private RegistrationTicketAssignment(
        Guid id,
        Guid tenantId,
        Guid registrationOrderId,
        Guid registrationOrderLineId,
        int ordinal,
        Guid? participantId,
        AssignmentStatusEnum status,
        DateTime? assignmentDeadline,
        DateTime createdAt)
    {
        Id = id;
        TenantId = tenantId;
        RegistrationOrderId = registrationOrderId;
        RegistrationOrderLineId = registrationOrderLineId;
        Ordinal = ordinal;
        ParticipantId = participantId;
        AssignmentStatusId = (int)status;
        AssignmentDeadline = assignmentDeadline;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(RegistrationTicketAssignment));
    }

    public Guid RegistrationOrderId { get; private set; }

    public RegistrationOrder? RegistrationOrder { get; private set; }

    public Guid RegistrationOrderLineId { get; private set; }

    public RegistrationOrderLine? RegistrationOrderLine { get; private set; }

    public Guid? ParticipantId { get; private set; }

    public RegistrationParticipant? Participant { get; private set; }

    public int Ordinal { get; private set; }

    public int AssignmentStatusId { get; private set; }

    public AssignmentStatus? AssignmentStatus { get; private set; }

    public DateTime? AssignmentDeadline { get; private set; }

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static RegistrationTicketAssignment Create(
        Guid tenantId,
        Guid registrationOrderId,
        Guid registrationOrderLineId,
        int ordinal,
        Guid? participantId,
        AssignmentStatusEnum status,
        DateTime? assignmentDeadline,
        DateTime createdAt) => Create(
        Guid.CreateVersion7(), tenantId, registrationOrderId, registrationOrderLineId, ordinal, participantId, status, assignmentDeadline, createdAt);

    public static RegistrationTicketAssignment Create(
        Guid id,
        Guid tenantId,
        Guid registrationOrderId,
        Guid registrationOrderLineId,
        int ordinal,
        Guid? participantId,
        AssignmentStatusEnum status,
        DateTime? assignmentDeadline,
        DateTime createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || registrationOrderId == Guid.Empty || registrationOrderLineId == Guid.Empty || participantId == Guid.Empty ||
            !Enum.IsDefined(status))
        {
            throw new ArgumentException("Ticket assignment identity and status are invalid.");
        }

        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        DateTime normalizedCreatedAt = EnsureUtc(createdAt, nameof(createdAt));
        DateTime? normalizedDeadline = assignmentDeadline.HasValue
            ? EnsureUtc(assignmentDeadline.Value, nameof(assignmentDeadline))
            : null;

        bool isValidState = status switch
        {
            AssignmentStatusEnum.Unassigned => participantId is null && normalizedDeadline is null,
            AssignmentStatusEnum.Assigned => participantId is not null && normalizedDeadline is null,
            AssignmentStatusEnum.Deferred => participantId is null && normalizedDeadline > normalizedCreatedAt,
            _ => false
        };
        if (!isValidState)
        {
            throw new ArgumentException("Ticket assignment status, participant, and deadline are inconsistent.");
        }

        return new RegistrationTicketAssignment(
            id, tenantId, registrationOrderId, registrationOrderLineId, ordinal, participantId, status, normalizedDeadline, normalizedCreatedAt);
    }

    public static RegistrationTicketAssignment CreateAssigned(
        Guid id,
        Guid registrationOrderLineId,
        int ordinal,
        RegistrationParticipant participant,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(participant);
        RegistrationTicketAssignment assignment = Create(
            id,
            participant.TenantId,
            participant.RegistrationOrderId,
            registrationOrderLineId,
            ordinal,
            participant.Id,
            AssignmentStatusEnum.Assigned,
            assignmentDeadline: null,
            createdAt);
        assignment.Participant = participant;
        return assignment;
    }

    internal void AttachToGraph(
        RegistrationOrder order,
        RegistrationOrderLine line,
        RegistrationParticipant? participant)
    {
        if (RegistrationOrder is not null && !ReferenceEquals(RegistrationOrder, order) ||
            RegistrationOrderLine is not null && !ReferenceEquals(RegistrationOrderLine, line) ||
            Participant is not null && !ReferenceEquals(Participant, participant))
        {
            throw new InvalidOperationException("Ticket assignment is already attached to another authority graph.");
        }

        RegistrationOrder = order;
        RegistrationOrderLine = line;
        Participant = participant;
    }

    public void Assign(RegistrationParticipant participant, Guid concurrencyStamp)
    {
        ArgumentNullException.ThrowIfNull(participant);
        if (participant.TenantId != TenantId || participant.RegistrationOrderId != RegistrationOrderId || concurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException("Assigned participant must belong to the same order.", nameof(participant));
        }

        ParticipantId = participant.Id;
        Participant = participant;
        AssignmentStatusId = (int)AssignmentStatusEnum.Assigned;
        AssignmentDeadline = null;
        ConcurrencyStamp = concurrencyStamp;
    }

    public void Defer(DateTime assignmentDeadline, DateTime evaluatedAt, Guid concurrencyStamp)
    {
        DateTime deadline = EnsureUtc(assignmentDeadline, nameof(assignmentDeadline));
        DateTime now = EnsureUtc(evaluatedAt, nameof(evaluatedAt));
        if (deadline <= now || concurrencyStamp == Guid.Empty)
        {
            throw new ArgumentException("A future assignment deadline and concurrency stamp are required.");
        }

        ParticipantId = null;
        Participant = null;
        AssignmentStatusId = (int)AssignmentStatusEnum.Deferred;
        AssignmentDeadline = deadline;
        ConcurrencyStamp = concurrencyStamp;
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
        }

        return value;
    }
}
