namespace Explore.Application.Features.AiAssistant.Plans;

public enum AiPlanStepStatus
{
    Proposed = 1,
    RequiresClarification = 2,
    ReadyForConfirmation = 3,
    Blocked = 4,
    Confirmed = 5,
    Executing = 6,
    Executed = 7,
    Failed = 8
}
