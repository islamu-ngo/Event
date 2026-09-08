namespace Explore.Blazor.Client.Contracts.Services;

public sealed record TicketPurchaseGovernanceSubmission(
    bool IsSuccess,
    bool SupportsHardCrossOrderCeiling,
    string EnforcementScopeCode);
