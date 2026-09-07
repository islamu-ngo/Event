namespace Explore.Application.DTOs.Payments;

public sealed record PaidCheckoutSaleControlDto
{
    public Guid TenantId { get; init; }
    public Guid? EventId { get; init; }
    public bool IsStopped { get; init; }
    public bool ResumeReviewPending { get; init; }
    public long Version { get; init; }
    public IReadOnlyList<PaidCheckoutSaleControlAuditDto> AuditTrail { get; init; } = [];
}

public sealed record PaidCheckoutSaleControlAuditDto
{
    public int Sequence { get; init; }
    public required string ActionCode { get; init; }
    public required string ReasonCode { get; init; }
    public DateTime OccurredAt { get; init; }
}

public sealed record PaidCheckoutSaleControlMutationDto
{
    public string ReasonCode { get; init; } = string.Empty;
}

public sealed record PaidCheckoutResumeReviewDto
{
    public bool Approved { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}

public sealed record PaidCheckoutReviewApprovalDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public int TriggerId { get; init; }
    public long? MaximumOrderAmountMinor { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }
}

public sealed record RequestPaidCheckoutReviewDto
{
    public int TriggerId { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public long? MaximumOrderAmountMinor { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}

public sealed record DecidePaidCheckoutReviewDto
{
    public bool Approved { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
}
