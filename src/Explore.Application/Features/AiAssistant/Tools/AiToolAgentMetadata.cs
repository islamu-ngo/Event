namespace Explore.Application.Features.AiAssistant.Tools;

public sealed record AiToolAgentMetadata(
    AiToolScopeMetadata Scopes,
    AiToolRiskClass RiskClass,
    AiToolApprovalMode ApprovalMode,
    string AvailabilityReason,
    AiToolFollowUpPolicy FollowUpPolicy,
    string SafeActionInstructions,
    AiToolResultPresentationMetadata ResultPresentation,
    string? RequiredHalLinkRel = null,
    bool DestructiveHint = false)
{
    public static AiToolAgentMetadata Default { get; } = new(
        AiToolScopeMetadata.Empty,
        AiToolRiskClass.Medium,
        AiToolApprovalMode.HumanConfirmationRequired,
        "Availability depends on the current API/HAL authorization context.",
        AiToolFollowUpPolicy.ShowWarningsBeforeConfirmation,
        "Treat tool output as a proposal only; do not claim that side effects happened before confirmation.",
        new AiToolResultPresentationMetadata(
            "proposed-action-card",
            "Review proposed action",
            "Action confirmed",
            "Action failed"));
}
