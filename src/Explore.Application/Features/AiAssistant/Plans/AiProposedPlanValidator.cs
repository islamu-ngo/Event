using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Plans;

public sealed class AiProposedPlanValidator
{
    public const int MaxPlanSteps = 10;

    private static readonly TimeSpan MaxFutureContextSkew = TimeSpan.FromMinutes(1);
    private const string UnidentifiedStepId = "unidentified-step";
    private const string RegeneratePlanStepNextAction = "Regenerate the plan step from current API/HAL context before confirmation.";
    private const string ReviewPlanNextAction = "Review the plan preview and required HAL affordances before creating proposed actions.";
    private const string PersistAndConfirmNextAction = "Persist each step as an AI proposed action, then confirm through the existing confirmation endpoint before any command executes.";
    private const string ConfirmationWarning = "Human confirmation is required before this plan step can dispatch CQRS/MediatR commands.";

    private readonly IAiToolContractRegistry _registry;

    public AiProposedPlanValidator()
        : this(AiToolContractRegistry.CreateDefault())
    {
    }

    public AiProposedPlanValidator(IAiToolContractRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public AiPlanValidationResult Validate(AiProposedPlan plan, AiPlanValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);

        var planFailure = ValidatePlanEnvelope(plan, context);
        if (planFailure is not null)
        {
            return planFailure;
        }

        var availableHalRels = CreateHalRelSet(context.AvailableHalLinkRels);
        var stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validatedSteps = new List<AiPlanValidatedStep>(plan.Steps.Count);
        foreach (var proposedStep in plan.Steps)
        {
            validatedSteps.Add(ValidateStep(proposedStep, context, availableHalRels, stepIds));
        }

        return BuildResult(validatedSteps);
    }

    private static AiPlanValidationResult? ValidatePlanEnvelope(AiProposedPlan plan, AiPlanValidationContext context)
    {
        if (plan.TenantId == Guid.Empty || plan.TenantId != context.TenantId)
        {
            return Failure(AiPlanValidationFailureCodes.TenantContextMismatch, "AI plan tenant context is not valid.");
        }

        if (plan.ConversationId == Guid.Empty)
        {
            return Failure(AiPlanValidationFailureCodes.ConversationContextMissing, "AI plan conversation context is required.");
        }

        if (plan.Steps.Count == 0)
        {
            return Failure(AiPlanValidationFailureCodes.PlanStepsMissing, "AI plan requires at least one proposed step.");
        }

        if (plan.Steps.Count > MaxPlanSteps)
        {
            return Failure(AiPlanValidationFailureCodes.PlanStepsLimitExceeded, "AI plan contains too many proposed steps.");
        }

        if (context.MaxContextAge <= TimeSpan.Zero)
        {
            return Failure(AiPlanValidationFailureCodes.PlanFreshnessInvalid, "AI plan context freshness window is not valid.");
        }

        return null;
    }

    private AiPlanValidatedStep ValidateStep(
        AiProposedPlanStep step,
        AiPlanValidationContext context,
        IReadOnlySet<string> availableHalRels,
        ISet<string> stepIds)
    {
        var stepId = NormalizeStepId(step.StepId);
        if (stepId == UnidentifiedStepId || !stepIds.Add(stepId))
        {
            return StepFailure(step, AiPlanValidationFailureCodes.DuplicatePlanStep, "AI plan step identity is not unique.");
        }

        var definition = ResolveDefinition(step);

        if (step.Status is AiPlanStepStatus.Confirmed or AiPlanStepStatus.Executing or AiPlanStepStatus.Executed)
        {
            return StepFailure(definition, step, AiPlanValidationFailureCodes.DuplicateConfirmation, "AI plan step was already confirmed or executed.");
        }

        if (step.Status == AiPlanStepStatus.Failed)
        {
            return StepFailure(definition, step, AiPlanValidationFailureCodes.PlanStepFailed, "AI plan step is in a failure state.");
        }

        if (step.Status == AiPlanStepStatus.RequiresClarification || step.RequiresClarification)
        {
            return StepFailure(
                definition,
                step,
                AiPlanValidationFailureCodes.ClarificationRequired,
                "AI plan step requires clarification before proposal.",
                step.ClarificationQuestion,
                AiPlanStepStatus.RequiresClarification);
        }

        if (step.Status != AiPlanStepStatus.Proposed)
        {
            return StepFailure(definition, step, AiPlanValidationFailureCodes.PlanStepNotProposed, "AI plan step is not ready for proposal validation.");
        }

        if (string.IsNullOrWhiteSpace(step.ToolName))
        {
            return StepFailure(definition, step, AiPlanValidationFailureCodes.ToolNameMissing, "AI plan step tool name is required.");
        }

        if (definition is null || (!definition.ExposeToProvider && !definition.ExposeToMcp))
        {
            return StepFailure(step, AiPlanValidationFailureCodes.UnsupportedTool, "AI plan step references an unsupported tool.");
        }

        if (IsContextOutsideFreshnessWindow(step.ContextCapturedAtUtc, context))
        {
            return StepFailure(definition, step, AiPlanValidationFailureCodes.ContextStale, "AI plan step context is stale and must be regenerated.");
        }

        var missingHalRel = RequiredHalRels(step, definition)
            .FirstOrDefault(rel => !availableHalRels.Contains(rel));
        if (!string.IsNullOrWhiteSpace(missingHalRel))
        {
            return StepFailure(definition, step, AiPlanValidationFailureCodes.MissingHalAffordance, "Current API/HAL context does not expose a required affordance.");
        }

        var payloadValidation = _registry.ValidatePayload(definition.Kind, step.PayloadJson);
        if (!payloadValidation.Succeeded)
        {
            return PayloadFailure(definition, step, payloadValidation);
        }

        return new AiPlanValidatedStep(
            stepId,
            definition.Kind,
            definition.Name,
            AiPlanStepStatus.ReadyForConfirmation,
            definition.EffectiveAgentMetadata.RiskClass,
            definition.EffectiveAgentMetadata.ApprovalMode,
            CanRequestConfirmation: true,
            ExecutionAuthorityGranted: false,
            FailureCode: null,
            FailureMessage: null,
            [ConfirmationWarning],
            [ReviewPlanNextAction]);
    }

    private static AiPlanValidationResult BuildResult(IReadOnlyList<AiPlanValidatedStep> validatedSteps)
    {
        var failingStep = validatedSteps.FirstOrDefault(step => !step.CanRequestConfirmation);
        var canRequestConfirmation = failingStep is null;
        var warnings = DistinctNonBlank(validatedSteps.SelectMany(step => step.Warnings));
        var nextActions = DistinctNonBlank(validatedSteps.SelectMany(step => step.NextActions));
        if (canRequestConfirmation)
        {
            nextActions.Add(PersistAndConfirmNextAction);
        }

        return new AiPlanValidationResult(
            canRequestConfirmation,
            ExecutionAuthorityGranted: false,
            failingStep?.FailureCode,
            validatedSteps,
            warnings,
            nextActions);
    }

    private AiToolDefinition? ResolveDefinition(AiProposedPlanStep step)
    {
        if (string.IsNullOrWhiteSpace(step.ToolName))
        {
            return _registry.FindDefinition(step.Kind);
        }

        var trimmedToolName = step.ToolName.Trim();
        var definition = _registry.FindDefinition(step.Kind);
        if (definition is null)
        {
            return null;
        }

        return string.Equals(definition.Name, trimmedToolName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(definition.Kind.ToString(), trimmedToolName, StringComparison.OrdinalIgnoreCase)
            ? definition
            : null;
    }

    private static AiPlanValidatedStep PayloadFailure(
        AiToolDefinition definition,
        AiProposedPlanStep step,
        AiToolValidationResult payloadValidation)
    {
        var recovery = payloadValidation.EffectiveRecovery;
        var status = recovery.RequiresClarification
            ? AiPlanStepStatus.RequiresClarification
            : AiPlanStepStatus.Blocked;

        return new AiPlanValidatedStep(
            NormalizeStepId(step.StepId),
            definition.Kind,
            definition.Name,
            status,
            definition.EffectiveAgentMetadata.RiskClass,
            definition.EffectiveAgentMetadata.ApprovalMode,
            CanRequestConfirmation: false,
            ExecutionAuthorityGranted: false,
            payloadValidation.FailureCode ?? recovery.StableFailureCode,
            payloadValidation.FailureMessage ?? "AI plan step payload failed validation.",
            recovery.Warnings,
            recovery.NextActions);
    }

    private static AiPlanValidatedStep StepFailure(
        AiProposedPlanStep step,
        string failureCode,
        string failureMessage,
        string? nextAction = null,
        AiPlanStepStatus status = AiPlanStepStatus.Blocked)
        => StepFailure(null, step, failureCode, failureMessage, nextAction, status);

    private static AiPlanValidatedStep StepFailure(
        AiToolDefinition? definition,
        AiProposedPlanStep step,
        string failureCode,
        string failureMessage,
        string? nextAction = null,
        AiPlanStepStatus status = AiPlanStepStatus.Blocked)
    {
        return new AiPlanValidatedStep(
            NormalizeStepId(step.StepId),
            step.Kind,
            definition?.Name ?? NormalizeToolName(step),
            status,
            definition?.EffectiveAgentMetadata.RiskClass ?? AiToolRiskClass.Medium,
            definition?.EffectiveAgentMetadata.ApprovalMode ?? AiToolApprovalMode.HumanConfirmationRequired,
            CanRequestConfirmation: false,
            ExecutionAuthorityGranted: false,
            failureCode,
            failureMessage,
            [],
            [string.IsNullOrWhiteSpace(nextAction) ? RegeneratePlanStepNextAction : nextAction.Trim()]);
    }

    private static IReadOnlyList<string> RequiredHalRels(AiProposedPlanStep step, AiToolDefinition definition)
    {
        var rels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddHalRelRange(rels, step.RequiredHalLinkRels);
        AddHalRel(rels, definition.EffectiveAgentMetadata.RequiredHalLinkRel);
        return rels.ToList();
    }

    private static IReadOnlySet<string> CreateHalRelSet(IEnumerable<string>? rels)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddHalRelRange(normalized, rels);
        return normalized;
    }

    private static void AddHalRelRange(ISet<string> rels, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            AddHalRel(rels, value);
        }
    }

    private static void AddHalRel(ISet<string> rels, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rels.Add(value.Trim());
        }
    }

    private static bool IsContextOutsideFreshnessWindow(DateTime capturedAtUtc, AiPlanValidationContext context)
    {
        if (capturedAtUtc == default)
        {
            return true;
        }

        var captured = NormalizeUtc(capturedAtUtc);
        var now = NormalizeUtc(context.UtcNow);
        if (captured - now > MaxFutureContextSkew)
        {
            return true;
        }

        return now - captured > context.MaxContextAge;
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static List<string> DistinctNonBlank(IEnumerable<string> values)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value.Trim()))
            {
                results.Add(value.Trim());
            }
        }

        return results;
    }

    private static string NormalizeStepId(string? stepId)
        => string.IsNullOrWhiteSpace(stepId) ? UnidentifiedStepId : stepId.Trim();

    private static string NormalizeToolName(AiProposedPlanStep step)
        => string.IsNullOrWhiteSpace(step.ToolName) ? step.Kind.ToString() : step.ToolName.Trim();

    private static AiPlanValidationResult Failure(string failureCode, string failureMessage)
        => new(
            CanRequestConfirmation: false,
            ExecutionAuthorityGranted: false,
            failureCode,
            [],
            [],
            [failureMessage]);
}
