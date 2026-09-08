using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RefundAttempt : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    private readonly List<RefundLineAllocation> _lines = [];

    private RefundAttempt()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid PaymentAttemptId { get; private set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid PaidOrderAcceptanceSnapshotId { get; private set; }
    public Guid? SourceCampaignId { get; private set; }
    public string ReservationSourceKey { get; private set; } = string.Empty;
    public string AuthorityCode { get; private set; } = string.Empty;
    public string ReasonCode { get; private set; } = string.Empty;
    public int RefundPolicyVersion { get; private set; }
    public string RefundPolicyText { get; private set; } = string.Empty;
    public string RefundPolicyLanguageTag { get; private set; } = string.Empty;
    public string ProviderCode { get; private set; } = string.Empty;
    public string ExternalAccountId { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public string ProviderPaymentId { get; private set; } = string.Empty;
    public string ProviderIdempotencyKey { get; private set; } = string.Empty;
    public RefundAllocation Allocation { get; private set; } = null!;
    public RefundAttemptStatusEnum Status { get; private set; }
    public string? ProviderRefundId { get; private set; }
    public string? LastProviderRequestId { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTime? BuyerRefundSucceededAt { get; private set; }
    public long ApplicationFeeRefundedAmountMinor { get; private set; }
    public DateTime LastObservedAt { get; private set; }
    public DateTime? SucceededAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }
    public IReadOnlyCollection<RefundLineAllocation> Lines => _lines.OrderBy(line => line.Ordinal).ToArray();

    public bool ReservesCapacity => BuyerRefundSucceededAt.HasValue ||
        Status is not (RefundAttemptStatusEnum.Failed or RefundAttemptStatusEnum.Cancelled);

    public static RefundAttempt Create(
        Guid id,
        Guid tenantId,
        Guid paymentAttemptId,
        PaidOrderAcceptanceSnapshot acceptance,
        string externalAccountId,
        string providerPaymentId,
        string providerIdempotencyKey,
        long requestedTotalMinor,
        DateTime createdAt,
        Guid? sourceCampaignId = null,
        string authorityCode = "system",
        string reasonCode = "operator_refund")
    {
        ArgumentNullException.ThrowIfNull(acceptance);
        if (id == Guid.Empty || tenantId == Guid.Empty || paymentAttemptId == Guid.Empty ||
            acceptance.TenantId != tenantId || string.IsNullOrWhiteSpace(externalAccountId) ||
            string.IsNullOrWhiteSpace(providerPaymentId) ||
            string.IsNullOrWhiteSpace(providerIdempotencyKey) || createdAt.Kind != DateTimeKind.Utc || sourceCampaignId == Guid.Empty ||
            string.IsNullOrWhiteSpace(authorityCode) || authorityCode.Length > 40 || authorityCode.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Length > 80 || reasonCode.Any(char.IsControl))
        {
            throw new ArgumentException("Valid pinned refund authority is required.");
        }

        RefundAllocation allocation = RefundAllocation.AllocatePartial(
            requestedTotalMinor,
            acceptance.OrganizerAmountMinor,
            acceptance.PlatformFeeMinor,
            acceptance.PlatformContributionMinor);
        var attempt = new RefundAttempt
        {
            Id = id,
            TenantId = tenantId,
            PaymentAttemptId = paymentAttemptId,
            RegistrationOrderId = acceptance.RegistrationOrderId,
            PaidOrderAcceptanceSnapshotId = acceptance.Id,
            SourceCampaignId = sourceCampaignId,
            ReservationSourceKey = sourceCampaignId.HasValue
                ? $"campaign:{sourceCampaignId.Value:N}"
                : $"refund:{id:N}",
            AuthorityCode = authorityCode.Trim().ToLowerInvariant(),
            ReasonCode = reasonCode.Trim().ToLowerInvariant(),
            RefundPolicyVersion = acceptance.RefundPolicyVersion,
            RefundPolicyText = acceptance.RefundPolicyText,
            RefundPolicyLanguageTag = acceptance.RefundPolicyLanguageTag,
            ProviderCode = acceptance.ProviderCode,
            ExternalAccountId = externalAccountId.Trim(),
            CurrencyCode = acceptance.CurrencyCode,
            ProviderPaymentId = providerPaymentId.Trim(),
            ProviderIdempotencyKey = providerIdempotencyKey.Trim(),
            Allocation = allocation,
            Status = RefundAttemptStatusEnum.Requested,
            LastObservedAt = createdAt,
            CreatedAt = createdAt,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        attempt._lines.AddRange(RefundLineAllocation.Allocate(tenantId, id, allocation, acceptance.Lines));
        return attempt;
    }

    public void ReallocateForReservation(
        IReadOnlyCollection<RefundAttempt> existingAttempts,
        PaidOrderAcceptanceSnapshot acceptance)
    {
        ArgumentNullException.ThrowIfNull(existingAttempts);
        ArgumentNullException.ThrowIfNull(acceptance);
        RefundAttempt[] active = existingAttempts.Where(existing => existing.ReservesCapacity).ToArray();
        if (acceptance.Id != PaidOrderAcceptanceSnapshotId ||
            acceptance.TenantId != TenantId)
        {
            throw new ArgumentException("Refund reallocation requires matching captured authority.", nameof(acceptance));
        }

        long requestedTotalMinor = Allocation.TotalMinor;
        Allocation = RefundAllocation.AllocateReservationDelta(
            active.Sum(existing => existing.Allocation.TotalMinor),
            requestedTotalMinor,
            acceptance.OrganizerAmountMinor,
            acceptance.PlatformFeeMinor,
            acceptance.PlatformContributionMinor,
            active.Sum(existing => existing.Allocation.OrganizerAmountMinor),
            active.Sum(existing => existing.Allocation.PlatformFeeMinor),
            active.Sum(existing => existing.Allocation.PlatformContributionMinor));
        _lines.Clear();
        _lines.AddRange(RefundLineAllocation.AllocateFromRemaining(
            TenantId, Id, Allocation, acceptance, existingAttempts));
    }

    public void MarkDispatchPending(DateTime observedAt, string? providerRequestId) =>
        Advance(RefundAttemptStatusEnum.DispatchPending, null, observedAt, providerRequestId);

    public void MarkPending(string providerRefundId, DateTime observedAt, string? providerRequestId) =>
        Advance(RefundAttemptStatusEnum.Pending, providerRefundId, observedAt, providerRequestId);

    public void MarkRequiresAction(string providerRefundId, DateTime observedAt, string? providerRequestId) =>
        Advance(RefundAttemptStatusEnum.RequiresAction, providerRefundId, observedAt, providerRequestId);

    public void MarkUnknown(DateTime observedAt, string? providerRequestId, string? providerRefundId = null) =>
        Advance(RefundAttemptStatusEnum.Unknown, providerRefundId, observedAt, providerRequestId);

    public void MarkProviderBlocked(DateTime observedAt, string? providerRequestId, string failureCode)
    {
        string normalized = failureCode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length is 0 or > 80 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("A bounded provider failure code is required.", nameof(failureCode));
        }
        Advance(RefundAttemptStatusEnum.RequiresAction, null, observedAt, providerRequestId);
        FailureCode = normalized;
    }

    public void MarkBuyerRefundSucceeded(string providerRefundId, DateTime observedAt, string? providerRequestId)
    {
        if (observedAt.Kind != DateTimeKind.Utc || observedAt < LastObservedAt)
        {
            throw new InvalidOperationException("Refund evidence must be monotonic and UTC.");
        }
        if (Status is RefundAttemptStatusEnum.Failed or RefundAttemptStatusEnum.Cancelled)
        {
            throw new InvalidOperationException("Definitive refund evidence cannot be contradicted.");
        }

        PinProviderRefundId(providerRefundId);
        BuyerRefundSucceededAt ??= observedAt;
        LastObservedAt = observedAt;
        LastProviderRequestId = string.IsNullOrWhiteSpace(providerRequestId)
            ? LastProviderRequestId
            : providerRequestId.Trim();
        UpdatedAt = observedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void RetryProviderBlocked(DateTime requestedAt)
    {
        if (Status != RefundAttemptStatusEnum.RequiresAction || FailureCode is null)
        {
            throw new InvalidOperationException("Only a provider-blocked refund can be retried.");
        }

        Advance(RefundAttemptStatusEnum.Unknown, ProviderRefundId, requestedAt, null);
    }

    public void MarkSucceeded(
        string providerRefundId,
        DateTime observedAt,
        string? providerRequestId,
        long? applicationFeeRefundedAmountMinor = null)
    {
        long expectedFeeRefund = checked(Allocation.PlatformFeeMinor + Allocation.PlatformContributionMinor);
        long refundedFee = applicationFeeRefundedAmountMinor ?? expectedFeeRefund;
        if (refundedFee != expectedFeeRefund)
        {
            throw new ArgumentOutOfRangeException(nameof(applicationFeeRefundedAmountMinor));
        }

        MarkBuyerRefundSucceeded(providerRefundId, observedAt, providerRequestId);
        ApplicationFeeRefundedAmountMinor = refundedFee;
        Advance(RefundAttemptStatusEnum.Succeeded, providerRefundId, observedAt, providerRequestId);
    }

    public void MarkFailed(string providerRefundId, DateTime observedAt, string? providerRequestId) =>
        Advance(RefundAttemptStatusEnum.Failed, providerRefundId, observedAt, providerRequestId);

    public void MarkCancelled(string providerRefundId, DateTime observedAt, string? providerRequestId) =>
        Advance(RefundAttemptStatusEnum.Cancelled, providerRefundId, observedAt, providerRequestId);

    private void Advance(
        RefundAttemptStatusEnum status,
        string? providerRefundId,
        DateTime observedAt,
        string? providerRequestId)
    {
        if (observedAt.Kind != DateTimeKind.Utc || observedAt < LastObservedAt)
        {
            throw new InvalidOperationException("Refund evidence must be monotonic and UTC.");
        }

        if (Status is RefundAttemptStatusEnum.Succeeded or RefundAttemptStatusEnum.Failed or RefundAttemptStatusEnum.Cancelled)
        {
            if (Status == status)
            {
                return;
            }

            throw new InvalidOperationException("Definitive refund evidence cannot be contradicted.");
        }
        if (BuyerRefundSucceededAt.HasValue &&
            status is RefundAttemptStatusEnum.Failed or RefundAttemptStatusEnum.Cancelled)
        {
            throw new InvalidOperationException("Provider evidence cannot undo a proven buyer refund.");
        }

        if (!string.IsNullOrWhiteSpace(providerRefundId))
        {
            PinProviderRefundId(providerRefundId);
        }

        Status = status;
        FailureCode = null;
        LastObservedAt = observedAt;
        LastProviderRequestId = string.IsNullOrWhiteSpace(providerRequestId) ? LastProviderRequestId : providerRequestId.Trim();
        SucceededAt = status == RefundAttemptStatusEnum.Succeeded ? observedAt : null;
        UpdatedAt = observedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private void PinProviderRefundId(string providerRefundId)
    {
        string normalized = providerRefundId?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 200 || normalized.Any(char.IsControl) ||
            ProviderRefundId is not null && !string.Equals(ProviderRefundId, normalized, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Provider refund identity cannot change.");
        }

        ProviderRefundId = normalized;
    }
}
