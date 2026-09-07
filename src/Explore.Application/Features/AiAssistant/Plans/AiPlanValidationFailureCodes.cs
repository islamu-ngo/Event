namespace Explore.Application.Features.AiAssistant.Plans;

public static class AiPlanValidationFailureCodes
{
    public const string TenantContextMismatch = "tenant_context_mismatch";
    public const string ConversationContextMissing = "conversation_context_missing";
    public const string PlanStepsMissing = "plan_steps_missing";
    public const string PlanStepsLimitExceeded = "plan_steps_limit_exceeded";
    public const string PlanFreshnessInvalid = "plan_freshness_invalid";
    public const string DuplicatePlanStep = "duplicate_plan_step";
    public const string DuplicateConfirmation = "duplicate_confirmation";
    public const string PlanStepFailed = "plan_step_failed";
    public const string ClarificationRequired = "clarification_required";
    public const string PlanStepNotProposed = "plan_step_not_proposed";
    public const string ToolNameMissing = "tool_name_missing";
    public const string UnsupportedTool = "unsupported_tool";
    public const string ContextStale = "context_stale";
    public const string MissingHalAffordance = "missing_hal_affordance";
}
