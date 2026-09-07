namespace Explore.Application.DTOs.OrganizerPaymentConnections;

public sealed record OrganizerPaymentConnectionDto
{
    public int StatusId { get; init; }
    public string? MerchantCountryCode { get; init; }
    public int ChargeCapabilityStateId { get; init; }
    public int RequirementsStateId { get; init; }
    public IReadOnlyList<string> SupportedCurrencyCodes { get; init; } = [];
    public DateTime? LastReadinessObservedAt { get; init; }
}
