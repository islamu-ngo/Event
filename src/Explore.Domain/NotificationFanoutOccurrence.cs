using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class NotificationFanoutOccurrence : ITenantEntity
{
    private NotificationFanoutOccurrence()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public Guid EventId { get; private set; }
    public Event? Event { get; private set; }
    public Guid? SessionId { get; private set; }
    public EventSession? Session { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public DateTime AudienceCutoffAt { get; private set; }
    public Guid AggregateVersion { get; private set; }
    public long EmailDeliveryPolicyRevision { get; private set; }
    public string ChangeSetJson { get; private set; } = null!;
    public string SafeBeforeSnapshotJson { get; private set; } = null!;
    public string SafeAfterSnapshotJson { get; private set; } = null!;
    public string TemplateKey { get; private set; } = null!;
    public int TemplateVersion { get; private set; }
    public int DeliveryPolicyId { get; private set; }
    public NotificationDeliveryPolicy? DeliveryPolicy { get; private set; }
    public int PolicyVersion { get; private set; }
    public int Priority { get; private set; }
    public DateTime NotBefore { get; private set; }
    public string SourceType { get; private set; } = null!;
    public Guid SourceId { get; private set; }
    public string CoalescingKey { get; private set; } = null!;
    public DateTime? CoalescingWindowEndsAt { get; private set; }
    public NotificationFanoutOccurrenceState State { get; private set; }
    public Guid? SupersededByOccurrenceId { get; private set; }
    public NotificationFanoutOccurrence? SupersededByOccurrence { get; private set; }
    public string? SuppressionReason { get; private set; }
    public DateTime? SupersededAt { get; private set; }

    public static NotificationFanoutOccurrence Create(
        Guid id,
        Guid tenantId,
        Guid eventId,
        Guid? sessionId,
        DateTime occurredAt,
        DateTime audienceCutoffAt,
        Guid aggregateVersion,
        string changeSetJson,
        string safeBeforeSnapshotJson,
        string safeAfterSnapshotJson,
        string templateKey,
        int templateVersion,
        int deliveryPolicyId,
        int policyVersion,
        int priority,
        DateTime notBefore,
        string sourceType,
        Guid sourceId,
        string coalescingKey,
        DateTime? coalescingWindowEndsAt,
        long emailDeliveryPolicyRevision)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(templateVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deliveryPolicyId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policyVersion);
        ArgumentOutOfRangeException.ThrowIfNegative(emailDeliveryPolicyRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeSetJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeBeforeSnapshotJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeAfterSnapshotJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(coalescingKey);

        if (id == Guid.Empty || tenantId == Guid.Empty || eventId == Guid.Empty
            || aggregateVersion == Guid.Empty || sourceId == Guid.Empty)
        {
            throw new ArgumentException("Fanout occurrence identifiers must be non-empty.");
        }

        return new NotificationFanoutOccurrence
        {
            Id = id,
            TenantId = tenantId,
            EventId = eventId,
            SessionId = sessionId,
            OccurredAt = occurredAt,
            AudienceCutoffAt = audienceCutoffAt,
            AggregateVersion = aggregateVersion,
            EmailDeliveryPolicyRevision = emailDeliveryPolicyRevision,
            ChangeSetJson = changeSetJson,
            SafeBeforeSnapshotJson = safeBeforeSnapshotJson,
            SafeAfterSnapshotJson = safeAfterSnapshotJson,
            TemplateKey = templateKey,
            TemplateVersion = templateVersion,
            DeliveryPolicyId = deliveryPolicyId,
            PolicyVersion = policyVersion,
            Priority = priority,
            NotBefore = notBefore,
            SourceType = sourceType,
            SourceId = sourceId,
            CoalescingKey = coalescingKey,
            CoalescingWindowEndsAt = coalescingWindowEndsAt,
            State = NotificationFanoutOccurrenceState.Pending,
        };
    }

    public void Supersede(Guid replacementOccurrenceId, string reason, DateTime supersededAt)
    {
        if (State != NotificationFanoutOccurrenceState.Pending)
        {
            throw new InvalidOperationException("Only a pending fanout occurrence can be superseded.");
        }

        if (replacementOccurrenceId == Guid.Empty || replacementOccurrenceId == Id)
        {
            throw new ArgumentException("The replacement occurrence must identify a different occurrence.", nameof(replacementOccurrenceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        State = NotificationFanoutOccurrenceState.Superseded;
        SupersededByOccurrenceId = replacementOccurrenceId;
        SuppressionReason = reason;
        SupersededAt = supersededAt;
    }
}

public enum NotificationFanoutOccurrenceState
{
    Pending = 1,
    Superseded = 2,
}
