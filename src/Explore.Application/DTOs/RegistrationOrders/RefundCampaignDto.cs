namespace Explore.Application.DTOs.RegistrationOrders;

public sealed record RefundCampaignDto
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public string KindCode { get; init; } = string.Empty;
    public string StatusCode { get; init; } = string.Empty;
    public DateTime DecisionAt { get; init; }
    public int TotalPaymentCount { get; init; }
    public int GeneratedCount { get; init; }
    public int PendingCount { get; init; }
    public int SucceededCount { get; init; }
    public int FailedCount { get; init; }
    public int UnknownCount { get; init; }
    public int OperatorCaseCount { get; init; }
}
