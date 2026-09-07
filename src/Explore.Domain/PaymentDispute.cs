using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class PaymentDispute : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    private PaymentDispute()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid PaymentAttemptId { get; private set; }
    public string ProviderDisputeId { get; private set; } = string.Empty;
    public PaymentDisputeStage Stage { get; private set; }
    public PaymentDisputeStatus Status { get; private set; }
    public long AmountMinor { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public DateTime LastObservedAt { get; private set; }
    public DateTime? ResponseDueAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public bool IsOpen => Status == PaymentDisputeStatus.Open;

    public static PaymentDispute Observe(
        Guid id,
        Guid tenantId,
        Guid paymentAttemptId,
        string providerDisputeId,
        PaymentDisputeStage stage,
        PaymentDisputeStatus status,
        long amountMinor,
        string currencyCode,
        DateTime observedAt,
        DateTime? responseDueAt = null)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || paymentAttemptId == Guid.Empty || amountMinor <= 0 ||
            string.IsNullOrWhiteSpace(providerDisputeId) || string.IsNullOrWhiteSpace(currencyCode) || observedAt.Kind != DateTimeKind.Utc ||
            responseDueAt?.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException("Valid tenant-bound dispute evidence is required.");
        }

        return new()
        {
            Id = id,
            TenantId = tenantId,
            PaymentAttemptId = paymentAttemptId,
            ProviderDisputeId = providerDisputeId.Trim(),
            Stage = stage,
            Status = status,
            AmountMinor = amountMinor,
            CurrencyCode = currencyCode.Trim().ToUpperInvariant(),
            LastObservedAt = observedAt,
            ResponseDueAt = responseDueAt.HasValue ? DateTime.SpecifyKind(responseDueAt.Value, DateTimeKind.Utc) : null,
            CreatedAt = observedAt,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    public bool ApplyProviderEvidence(
        PaymentDisputeStage stage,
        PaymentDisputeStatus status,
        long amountMinor,
        string currencyCode,
        DateTime observedAt,
        DateTime? responseDueAt = null)
    {
        string normalizedCurrency = currencyCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (observedAt.Kind != DateTimeKind.Utc || amountMinor != AmountMinor ||
            !string.Equals(normalizedCurrency, CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Dispute evidence must preserve UTC money authority.");
        }
        if (observedAt < LastObservedAt)
        {
            return false;
        }
        bool currentTerminal = Status != PaymentDisputeStatus.Open;
        bool incomingTerminal = status != PaymentDisputeStatus.Open;
        if (currentTerminal)
        {
            if (status != Status && incomingTerminal)
            {
                throw new InvalidOperationException("Terminal dispute evidence cannot be contradicted.");
            }
            return false;
        }
        if (observedAt == LastObservedAt && status == Status && stage <= Stage)
        {
            return false;
        }

        if (stage > Stage)
        {
            Stage = stage;
        }
        Status = status;
        ResponseDueAt = incomingTerminal
            ? null
            : responseDueAt.HasValue
                ? DateTime.SpecifyKind(responseDueAt.Value, DateTimeKind.Utc)
                : ResponseDueAt;
        LastObservedAt = observedAt;
        UpdatedAt = observedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }
}
