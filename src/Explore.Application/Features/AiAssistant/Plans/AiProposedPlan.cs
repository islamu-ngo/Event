namespace Explore.Application.Features.AiAssistant.Plans;

public sealed record AiProposedPlan(
    Guid TenantId,
    Guid ConversationId,
    IReadOnlyList<AiProposedPlanStep> Steps);
