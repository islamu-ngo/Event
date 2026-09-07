using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Plans;

public sealed record AiProposedPlanStep(
    string StepId,
    AiProposedActionKind Kind,
    string ToolName,
    string PayloadJson,
    DateTime ContextCapturedAtUtc,
    AiPlanStepStatus Status = AiPlanStepStatus.Proposed,
    IReadOnlySet<string>? RequiredHalLinkRels = null,
    bool RequiresClarification = false,
    string? ClarificationQuestion = null);
