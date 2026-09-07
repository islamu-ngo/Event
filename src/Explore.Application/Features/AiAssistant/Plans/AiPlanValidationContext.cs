namespace Explore.Application.Features.AiAssistant.Plans;

public sealed record AiPlanValidationContext(
    Guid TenantId,
    IReadOnlySet<string> AvailableHalLinkRels,
    DateTime UtcNow,
    TimeSpan MaxContextAge);
