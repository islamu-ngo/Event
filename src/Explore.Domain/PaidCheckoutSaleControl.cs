using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class PaidCheckoutSaleControl : ITenantEntity, IAuditableEntity
{
    private readonly List<PaidCheckoutSaleControlAudit> _auditTrail = [];

    private PaidCheckoutSaleControl()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid? EventId { get; private set; }
    public string ScopeKey { get; private set; } = string.Empty;
    public bool IsStopped { get; private set; }
    public Guid? ResumeRequestedBy { get; private set; }
    public DateTime? ResumeRequestedAt { get; private set; }
    public Guid? ResumeReviewedBy { get; private set; }
    public DateTime? ResumeReviewedAt { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public IReadOnlyCollection<PaidCheckoutSaleControlAudit> AuditTrail => _auditTrail.OrderBy(entry => entry.Sequence).ToArray();

    public static PaidCheckoutSaleControl CreateActive(
        Guid tenantId,
        Guid? eventId,
        Guid actorUserId,
        DateTime occurredAt) => Create(tenantId, eventId, actorUserId, false, "activated", occurredAt);

    public static PaidCheckoutSaleControl CreateStopped(
        Guid tenantId,
        Guid? eventId,
        Guid actorUserId,
        string reasonCode,
        DateTime occurredAt) => Create(tenantId, eventId, actorUserId, true, NormalizeReason(reasonCode), occurredAt);

    private static PaidCheckoutSaleControl Create(
        Guid tenantId,
        Guid? eventId,
        Guid actorUserId,
        bool stopped,
        string reasonCode,
        DateTime occurredAt)
    {
        DateTime timestamp = Ensure(tenantId, eventId, actorUserId, occurredAt);
        var control = new PaidCheckoutSaleControl
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventId = eventId,
            ScopeKey = eventId is { } value ? $"event:{value:N}" : "tenant",
            IsStopped = stopped,
            Version = 1,
            CreatedAt = timestamp,
            CreatedBy = actorUserId
        };
        control.Append(stopped ? "stopped" : "activated", reasonCode, actorUserId, null, timestamp);
        return control;
    }

    public bool Stop(Guid actorUserId, string reasonCode, DateTime occurredAt)
    {
        DateTime timestamp = Ensure(TenantId, EventId, actorUserId, occurredAt);
        string reason = NormalizeReason(reasonCode);
        if (IsStopped && ResumeRequestedBy is null)
        {
            return false;
        }

        IsStopped = true;
        ResumeRequestedBy = null;
        ResumeRequestedAt = null;
        ResumeReviewedBy = null;
        ResumeReviewedAt = null;
        Mutated(actorUserId, timestamp);
        Append("stopped", reason, actorUserId, null, timestamp);
        return true;
    }

    public void RequestResume(Guid actorUserId, string reasonCode, DateTime occurredAt)
    {
        DateTime timestamp = Ensure(TenantId, EventId, actorUserId, occurredAt);
        if (!IsStopped || ResumeRequestedBy is not null)
        {
            throw new InvalidOperationException("A resume review can be requested only once for a stopped sale control.");
        }

        ResumeRequestedBy = actorUserId;
        ResumeRequestedAt = timestamp;
        ResumeReviewedBy = null;
        ResumeReviewedAt = null;
        Mutated(actorUserId, timestamp);
        Append("resume_requested", NormalizeReason(reasonCode), actorUserId, null, timestamp);
    }

    public void ReviewResume(Guid reviewerUserId, bool approved, string reasonCode, DateTime occurredAt)
    {
        DateTime timestamp = Ensure(TenantId, EventId, reviewerUserId, occurredAt);
        if (!IsStopped || ResumeRequestedBy is not { } requester)
        {
            throw new InvalidOperationException("No stopped sale-control resume request is pending review.");
        }
        if (reviewerUserId == requester)
        {
            throw new InvalidOperationException("Resume review requires a user different from the requester.");
        }

        ResumeReviewedBy = reviewerUserId;
        ResumeReviewedAt = timestamp;
        IsStopped = !approved;
        ResumeRequestedBy = null;
        ResumeRequestedAt = null;
        Mutated(reviewerUserId, timestamp);
        Append(approved ? "resume_approved" : "resume_rejected", NormalizeReason(reasonCode), reviewerUserId, requester, timestamp);
    }

    private void Mutated(Guid actorUserId, DateTime timestamp)
    {
        Version = checked(Version + 1);
        UpdatedAt = timestamp;
        UpdatedBy = actorUserId;
    }

    private void Append(string actionCode, string reasonCode, Guid actorUserId, Guid? subjectUserId, DateTime occurredAt) =>
        _auditTrail.Add(PaidCheckoutSaleControlAudit.Create(
            TenantId, Id, checked(_auditTrail.Count + 1), EventId, actionCode, reasonCode, actorUserId, subjectUserId, occurredAt));

    private static DateTime Ensure(Guid tenantId, Guid? eventId, Guid actorUserId, DateTime occurredAt)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty || actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Sale-control tenant, optional event, and actor identities must be valid.");
        }
        return OrganizerPaymentProviderConnection.EnsureUtc(occurredAt, nameof(occurredAt));
    }

    internal static string NormalizeReason(string? value)
    {
        string normalized = value?.Trim().ToLowerInvariant().Replace(' ', '_') ?? string.Empty;
        if (normalized.Length is 0 or > 80 || normalized.Any(character =>
                character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')))
        {
            throw new ArgumentException("Sale-control reason must be a bounded machine-consumed code.", nameof(value));
        }
        return normalized;
    }
}

public sealed class PaidCheckoutSaleControlAudit : ITenantEntity
{
    private PaidCheckoutSaleControlAudit()
    {
    }

    public Guid TenantId { get; set; }
    public Guid PaidCheckoutSaleControlId { get; private set; }
    public int Sequence { get; private set; }
    public Guid? EventId { get; private set; }
    public string ActionCode { get; private set; } = string.Empty;
    public string ReasonCode { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public Guid? SubjectUserId { get; private set; }
    public DateTime OccurredAt { get; private set; }

    internal static PaidCheckoutSaleControlAudit Create(
        Guid tenantId,
        Guid controlId,
        int sequence,
        Guid? eventId,
        string actionCode,
        string reasonCode,
        Guid actorUserId,
        Guid? subjectUserId,
        DateTime occurredAt) => new()
        {
            TenantId = tenantId,
            PaidCheckoutSaleControlId = controlId,
            Sequence = sequence,
            EventId = eventId,
            ActionCode = actionCode,
            ReasonCode = reasonCode,
            ActorUserId = actorUserId,
            SubjectUserId = subjectUserId,
            OccurredAt = occurredAt
        };
}
