namespace Explore.Application.Features.AiAssistant.Plans;

public sealed record AiPlanValidationResult(
    bool CanRequestConfirmation,
    bool ExecutionAuthorityGranted,
    string? FailureCode,
    IReadOnlyList<AiPlanValidatedStep> Steps,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> NextActions);
