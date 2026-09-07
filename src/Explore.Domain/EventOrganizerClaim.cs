using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class EventOrganizerClaim : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public Guid EventId { get; private set; }
    public Event? Event { get; private set; }
    public Guid ClaimantActorId { get; private set; }
    public Actor? ClaimantActor { get; private set; }
    public int StatusId { get; private set; }
    public EventOrganizerClaimStatus? Status { get; private set; }
    public string EvidenceType { get; private set; } = string.Empty;
    public string EvidenceReference { get; private set; } = string.Empty;
    public Guid? ReviewerUserId { get; private set; }
    public User? ReviewerUser { get; private set; }
    public string? DecisionReasonCode { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public static EventOrganizerClaim CreatePending(
        Guid tenantId,
        Guid eventId,
        Guid claimantActorId,
        string evidenceType,
        string evidenceReference,
        DateTime now)
    {
        EnsureIdentifier(tenantId, nameof(tenantId));
        EnsureIdentifier(eventId, nameof(eventId));
        EnsureIdentifier(claimantActorId, nameof(claimantActorId));

        return new EventOrganizerClaim
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventId = eventId,
            ClaimantActorId = claimantActorId,
            StatusId = (int)EventOrganizerClaimStatusEnum.Pending,
            EvidenceType = NormalizeRequired(evidenceType, nameof(evidenceType)),
            EvidenceReference = NormalizeRequired(evidenceReference, nameof(evidenceReference)),
            CreatedAt = EnsureUtc(now)
        };
    }

    public void RequestEvidence(Guid reviewerUserId, string reasonCode, DateTime now)
    {
        EnsureStatus(EventOrganizerClaimStatusEnum.Pending);
        RecordDecision(EventOrganizerClaimStatusEnum.EvidenceRequired, reviewerUserId, reasonCode, now);
    }

    public void Approve(Event @event, Guid reviewerUserId, string reasonCode, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(@event);
        EnsureReviewable();

        if (@event.Id != EventId || @event.TenantId != TenantId)
        {
            throw new InvalidOperationException("Organizer claim and event must belong to the same tenant and event.");
        }

        if (@event.OrganizerActorId is { } organizerActorId && organizerActorId != ClaimantActorId)
        {
            throw new InvalidOperationException("Event already has a different organizer actor.");
        }

        @event.OrganizerActorId = ClaimantActorId;
        RecordDecision(EventOrganizerClaimStatusEnum.Approved, reviewerUserId, reasonCode, now);
    }

    public void Reject(Guid reviewerUserId, string reasonCode, DateTime now)
    {
        EnsureReviewable();
        RecordDecision(EventOrganizerClaimStatusEnum.Rejected, reviewerUserId, reasonCode, now);
    }

    public void Withdraw(DateTime now)
    {
        EnsureReviewable();
        StatusId = (int)EventOrganizerClaimStatusEnum.Withdrawn;
        DecidedAt = EnsureUtc(now);
    }

    public void Expire(DateTime now)
    {
        EnsureReviewable();
        StatusId = (int)EventOrganizerClaimStatusEnum.Expired;
        DecidedAt = EnsureUtc(now);
    }

    private void RecordDecision(
        EventOrganizerClaimStatusEnum status,
        Guid reviewerUserId,
        string reasonCode,
        DateTime now)
    {
        EnsureIdentifier(reviewerUserId, nameof(reviewerUserId));
        StatusId = (int)status;
        ReviewerUserId = reviewerUserId;
        DecisionReasonCode = NormalizeRequired(reasonCode, nameof(reasonCode));
        DecidedAt = EnsureUtc(now);
    }

    private void EnsureReviewable()
    {
        if (StatusId is not ((int)EventOrganizerClaimStatusEnum.Pending)
            and not ((int)EventOrganizerClaimStatusEnum.EvidenceRequired))
        {
            throw new InvalidOperationException("Organizer claim is no longer reviewable.");
        }
    }

    private void EnsureStatus(EventOrganizerClaimStatusEnum expected)
    {
        if (StatusId != (int)expected)
        {
            throw new InvalidOperationException($"Organizer claim must be {expected} for this transition.");
        }
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }

    private static void EnsureIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier is required.", parameterName);
        }
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", nameof(value));
        }

        return value;
    }
}
