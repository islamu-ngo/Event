using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationAmendment : ITenantEntity, IAuditableEntity
{
    private RegistrationAmendment() { }

    private RegistrationAmendment(Guid tenantId, Guid eventId, Guid orderId, Guid? actorUserId, string reason,
        Guid lineId, int ordinal, Guid? beforeParticipantId, int? beforeStatusId, Guid afterParticipantId, int afterStatusId, DateTime occurredAt)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        EventId = eventId;
        RegistrationOrderId = orderId;
        ActorUserId = actorUserId;
        Reason = reason.Trim()[..Math.Min(reason.Trim().Length, 500)];
        ChangeKind = "ticket-assignment";
        RegistrationOrderLineId = lineId;
        Ordinal = ordinal;
        BeforeParticipantId = beforeParticipantId;
        BeforeAssignmentStatusId = beforeStatusId;
        AfterParticipantId = afterParticipantId;
        AfterAssignmentStatusId = afterStatusId;
        OccurredAt = occurredAt.Kind == DateTimeKind.Utc ? occurredAt : throw new ArgumentException("Amendment timestamp must be UTC.");
        CreatedAt = OccurredAt;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Event? Event { get; private set; }
    public Guid RegistrationOrderId { get; private set; }
    public RegistrationOrder? RegistrationOrder { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string ChangeKind { get; private set; } = string.Empty;
    public Guid RegistrationOrderLineId { get; private set; }
    public int Ordinal { get; private set; }
    public string Source { get; private set; } = "manual";
    public string LineageKey { get; private set; } = string.Empty;
    public Guid? BeforeParticipantId { get; private set; }
    public int? BeforeAssignmentStatusId { get; private set; }
    public Guid? AfterParticipantId { get; private set; }
    public int AfterAssignmentStatusId { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static RegistrationAmendment CreateAssignmentChange(Guid tenantId, Guid eventId, Guid orderId, Guid? actorUserId,
        string reason, Guid lineId, int ordinal, Guid? beforeParticipantId, int? beforeStatusId,
        Guid afterParticipantId, int afterStatusId, DateTime occurredAt)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty || orderId == Guid.Empty || lineId == Guid.Empty ||
            actorUserId == Guid.Empty || afterParticipantId == Guid.Empty || ordinal <= 0 || afterStatusId <= 0 ||
            string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Registration amendment facts are invalid.");
        }

        return new RegistrationAmendment(tenantId, eventId, orderId, actorUserId, reason, lineId, ordinal,
            beforeParticipantId, beforeStatusId, afterParticipantId, afterStatusId, occurredAt);
    }

    public static RegistrationAmendment CreateCompanyCsvAssignmentChange(Guid tenantId, Guid eventId, Guid orderId, Guid? actorUserId,
        string lineageKey, Guid lineId, int ordinal, Guid? beforeParticipantId, int? beforeStatusId,
        Guid afterParticipantId, int afterStatusId, DateTime occurredAt)
    {
        RegistrationAmendment amendment = CreateAssignmentChange(tenantId, eventId, orderId, actorUserId,
            "company-csv-assignment", lineId, ordinal, beforeParticipantId, beforeStatusId, afterParticipantId, afterStatusId, occurredAt);
        amendment.Source = "company-csv";
        amendment.LineageKey = string.IsNullOrWhiteSpace(lineageKey) || lineageKey.Length > 128
            ? throw new ArgumentException("Registration amendment lineage key is invalid.", nameof(lineageKey))
            : lineageKey.Trim();
        return amendment;
    }
}
