using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Plans;

public sealed record AiPlanValidatedStep(
    string StepId,
    AiProposedActionKind Kind,
    string ToolName,
    AiPlanStepStatus Status,
    AiToolRiskClass RiskClass,
    AiToolApprovalMode ApprovalMode,
    bool CanRequestConfirmation,
    bool ExecutionAuthorityGranted,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> NextActions);
