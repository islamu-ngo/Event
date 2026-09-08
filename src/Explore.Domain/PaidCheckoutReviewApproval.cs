using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public enum PaidCheckoutReviewTrigger
{
    FirstPaidEvent = 1,
    HighValue = 2
}

public sealed class PaidCheckoutReviewApproval : ITenantEntity, IAuditableEntity
{
    private PaidCheckoutReviewApproval()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid OrganizerActorId { get; private set; }
    public Guid PaidEventPolicyVersionId { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public int TriggerId { get; private set; }
    public PaidCheckoutReviewTrigger Trigger => (PaidCheckoutReviewTrigger)TriggerId;
    public long? MaximumOrderAmountMinor { get; private set; }
    public string StatusCode { get; private set; } = string.Empty;
    public string RequestReasonCode { get; private set; } = string.Empty;
    public Guid RequestedByUserId { get; private set; }
    public DateTime RequestedAt { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public string? ReviewReasonCode { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static PaidCheckoutReviewApproval Request(
        Guid tenantId,
        Guid eventId,
        Guid organizerActorId,
        Guid policyVersionId,
        string currencyCode,
        PaidCheckoutReviewTrigger trigger,
        long? maximumOrderAmountMinor,
        Guid requesterUserId,
        string reasonCode,
        DateTime requestedAt)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty || organizerActorId == Guid.Empty ||
            policyVersionId == Guid.Empty || requesterUserId == Guid.Empty || !Enum.IsDefined(trigger))
        {
            throw new ArgumentException("Review request lineage and trigger are required.");
        }
        if (trigger == PaidCheckoutReviewTrigger.HighValue != maximumOrderAmountMinor.HasValue || maximumOrderAmountMinor is <= 0)
        {
            throw new ArgumentException("High-value review requires an explicit positive maximum amount and first-event review does not.");
        }

        CurrencyMetadata currency = CurrencyMetadata.Get(currencyCode);
        if (currency.IsNoCurrency)
        {
            throw new ArgumentException("Review approval requires an exact monetary currency.", nameof(currencyCode));
        }
        DateTime timestamp = OrganizerPaymentProviderConnection.EnsureUtc(requestedAt, nameof(requestedAt));
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventId = eventId,
            OrganizerActorId = organizerActorId,
            PaidEventPolicyVersionId = policyVersionId,
            CurrencyCode = currency.Code,
            TriggerId = (int)trigger,
            MaximumOrderAmountMinor = maximumOrderAmountMinor,
            StatusCode = "pending",
            RequestReasonCode = PaidCheckoutSaleControl.NormalizeReason(reasonCode),
            RequestedByUserId = requesterUserId,
            RequestedAt = timestamp,
            CreatedAt = timestamp,
            CreatedBy = requesterUserId
        };
    }

    public void Approve(Guid reviewerUserId, string reasonCode, DateTime reviewedAt) => Review(reviewerUserId, true, reasonCode, reviewedAt);

    public void Reject(Guid reviewerUserId, string reasonCode, DateTime reviewedAt) => Review(reviewerUserId, false, reasonCode, reviewedAt);

    public bool Authorizes(
        Guid policyVersionId,
        string currencyCode,
        PaidCheckoutReviewTrigger trigger,
        long orderAmountMinor) =>
        StatusCode == "approved" && PaidEventPolicyVersionId == policyVersionId && Trigger == trigger &&
        string.Equals(CurrencyCode, currencyCode, StringComparison.Ordinal) && orderAmountMinor > 0 &&
        (trigger == PaidCheckoutReviewTrigger.FirstPaidEvent || MaximumOrderAmountMinor is { } maximum && orderAmountMinor <= maximum);

    private void Review(Guid reviewerUserId, bool approved, string reasonCode, DateTime reviewedAt)
    {
        if (StatusCode != "pending")
        {
            throw new InvalidOperationException("Only a pending paid Checkout review can be decided.");
        }
        if (reviewerUserId == Guid.Empty || reviewerUserId == RequestedByUserId)
        {
            throw new InvalidOperationException("Review requires a different authenticated user.");
        }

        DateTime timestamp = OrganizerPaymentProviderConnection.EnsureUtc(reviewedAt, nameof(reviewedAt));
        StatusCode = approved ? "approved" : "rejected";
        ReviewedByUserId = reviewerUserId;
        ReviewReasonCode = PaidCheckoutSaleControl.NormalizeReason(reasonCode);
        ReviewedAt = timestamp;
        UpdatedAt = timestamp;
        UpdatedBy = reviewerUserId;
    }
}
