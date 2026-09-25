using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class EventResourceAuditEntry : ITenantEntity
{
    private Guid _tenantId;

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceAuditEntry));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventResourceAuditEntry));
    }
    public Guid EventResourceId { get; private set; }
    public Guid? ResponsibleManagerUserId { get; private set; }
    public EventResourceAuditAction Action { get; private set; }
    public EventResourceAuditOutcome Outcome { get; private set; }
    public EventResourceAuditReason Reason { get; private set; }
    public DateTime Timestamp { get; private set; }

    private EventResourceAuditEntry() { }

    public static EventResourceAuditEntry Create(Guid tenantId, Guid resourceId, Guid? managerUserId,
        EventResourceAuditAction action, EventResourceAuditOutcome outcome, EventResourceAuditReason reason,
        DateTime timestampUtc)
    {
        if (tenantId == Guid.Empty || resourceId == Guid.Empty || managerUserId == Guid.Empty
            || !Enum.IsDefined(action) || !Enum.IsDefined(outcome) || !Enum.IsDefined(reason)
            || timestampUtc == default || timestampUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Resource audit requires valid ownership, closed values and a UTC timestamp.");
        }

        return new EventResourceAuditEntry
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventResourceId = resourceId,
            ResponsibleManagerUserId = managerUserId,
            Action = action,
            Outcome = outcome,
            Reason = reason,
            Timestamp = timestampUtc
        };
    }

    public void EraseAttribution(Guid subjectUserId)
    {
        if (ResponsibleManagerUserId == subjectUserId)
        {
            ResponsibleManagerUserId = null;
        }
    }
}

public enum EventResourceAuditAction
{
    Create = 1,
    UpdateMetadata = 2,
    ReplacePolicy = 3,
    ConfigureDelivery = 4,
    Publish = 5,
    Withdraw = 6,
    Republish = 7,
    Archive = 8,
    Delete = 9,
    Moderate = 10
}

public enum EventResourceAuditOutcome
{
    Succeeded = 1,
    Rejected = 2,
    Conflict = 3
}

public enum EventResourceAuditReason
{
    OrganizerMutation = 1,
    GovernanceTightening = 2,
    Moderation = 3,
    ParentLifecycle = 4,
    PrivacyErasure = 5
}
