using Explore.Application.Features.AiAssistant.Plans;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Plans;

public sealed class AiProposedPlanValidatorTests
{
    [Test]
    public async Task ValidateWhenPlanIsReadyReturnsConfirmationPreviewWithoutExecutionAuthority()
    {
        var result = new AiProposedPlanValidator().Validate(
            CreatePlan(),
            CreateContext(halRels: HalSet("create-event")));

        await Assert.That(result.CanRequestConfirmation).IsTrue();
        await Assert.That(result.ExecutionAuthorityGranted).IsFalse();
        await Assert.That(result.FailureCode).IsNull();
        await Assert.That(result.Steps.Single().Status).IsEqualTo(AiPlanStepStatus.ReadyForConfirmation);
        await Assert.That(result.Steps.Single().ExecutionAuthorityGranted).IsFalse();
        await Assert.That(string.Join(" ", result.NextActions)).Contains("existing confirmation endpoint");
    }

    [Test]
    public async Task ValidateWhenContextIsStaleBlocksStepWithoutEchoingPayload()
    {
        var plan = CreatePlan(step: CreateStep(capturedAtUtc: UtcNow.AddHours(-2)));

        var result = new AiProposedPlanValidator().Validate(
            plan,
            CreateContext(halRels: HalSet("create-event"), maxContextAge: TimeSpan.FromMinutes(5)));

        await Assert.That(result.CanRequestConfirmation).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(AiPlanValidationFailureCodes.ContextStale);
        await Assert.That(result.Steps.Single().Status).IsEqualTo(AiPlanStepStatus.Blocked);
        await Assert.That(result.Steps.Single().FailureMessage).DoesNotContain("Community dinner");
    }

    [Test]
    public async Task ValidateWhenHalAffordanceIsMissingBlocksStep()
    {
        var result = new AiProposedPlanValidator().Validate(CreatePlan(), CreateContext());

        await Assert.That(result.CanRequestConfirmation).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(AiPlanValidationFailureCodes.MissingHalAffordance);
        await Assert.That(result.Steps.Single().CanRequestConfirmation).IsFalse();
        await Assert.That(result.Steps.Single().ExecutionAuthorityGranted).IsFalse();
    }

    [Test]
    public async Task ValidateWhenHalAffordanceHasDifferentCaseStillAllowsPreview()
    {
        var result = new AiProposedPlanValidator().Validate(
            CreatePlan(),
            CreateContext(halRels: HalSet("CREATE-EVENT")));

        await Assert.That(result.CanRequestConfirmation).IsTrue();
        await Assert.That(result.ExecutionAuthorityGranted).IsFalse();
    }

    [Test]
    public async Task ValidateWhenToolIsUnsupportedBlocksPlan()
    {
        var result = new AiProposedPlanValidator(new AiToolContractRegistry([])).Validate(
            CreatePlan(),
            CreateContext(halRels: HalSet("create-event")));

        await Assert.That(result.CanRequestConfirmation).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(AiPlanValidationFailureCodes.UnsupportedTool);
    }

    [Test]
    public async Task ValidateWhenToolNameIsBlankBlocksPlan()
    {
        var plan = CreatePlan(step: CreateStep(toolName: " "));

        var result = new AiProposedPlanValidator().Validate(
            plan,
            CreateContext(halRels: HalSet("create-event")));

        await Assert.That(result.CanRequestConfirmation).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(AiPlanValidationFailureCodes.ToolNameMissing);
    }

    [Test]
    public async Task ValidateWhenStepWasAlreadyConfirmedBlocksDuplicateConfirmation()
    {
        var plan = CreatePlan(step: CreateStep(status: AiPlanStepStatus.Confirmed));

        var result = new AiProposedPlanValidator().Validate(
            plan,
            CreateContext(halRels: HalSet("create-event")));

        await Assert.That(result.CanRequestConfirmation).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(AiPlanValidationFailureCodes.DuplicateConfirmation);
        await Assert.That(result.Steps.Single().Status).IsEqualTo(AiPlanStepStatus.Blocked);
    }

    [Test]
    public async Task ValidateWhenDuplicateStepIdsExistBlocksPlan()
    {
        var plan = new AiProposedPlan(
            TenantId,
            ConversationId,
            [CreateStep(stepId: "step-1"), CreateStep(stepId: "step-1")]);

        var result = new AiProposedPlanValidator().Validate(
            plan,
            CreateContext(halRels: HalSet("create-event")));

        await Assert.That(result.CanRequestConfirmation).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(AiPlanValidationFailureCodes.DuplicatePlanStep);
    }

    [Test]
    public async Task ValidateWhenStepRequiresClarificationKeepsClarificationNextAction()
    {
        var plan = CreatePlan(step: CreateStep(
            status: AiPlanStepStatus.RequiresClarification,
            requiresClarification: true,
            clarificationQuestion: "Which organization should own the event?"));

        var result = new AiProposedPlanValidator().Validate(
            plan,
            CreateContext(halRels: HalSet("create-event")));

        await Assert.That(result.CanRequestConfirmation).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(AiPlanValidationFailureCodes.ClarificationRequired);
        await Assert.That(result.Steps.Single().Status).IsEqualTo(AiPlanStepStatus.RequiresClarification);
        await Assert.That(result.Steps.Single().NextActions.Single()).IsEqualTo("Which organization should own the event?");
    }

    [Test]
    public async Task ValidateWhenStepIsFailedBlocksPlan()
    {
        var plan = CreatePlan(step: CreateStep(status: AiPlanStepStatus.Failed));

        var result = new AiProposedPlanValidator().Validate(
            plan,
            CreateContext(halRels: HalSet("create-event")));

        await Assert.That(result.CanRequestConfirmation).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(AiPlanValidationFailureCodes.PlanStepFailed);
    }

    [Test]
    public async Task ValidateWhenContextCapturedInFutureBlocksPlan()
    {
        var plan = CreatePlan(step: CreateStep(capturedAtUtc: UtcNow.AddMinutes(5)));

        var result = new AiProposedPlanValidator().Validate(
            plan,
            CreateContext(halRels: HalSet("create-event")));

        await Assert.That(result.CanRequestConfirmation).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(AiPlanValidationFailureCodes.ContextStale);
    }

    [Test]
    public async Task ValidateWhenPayloadNeedsClarificationBlocksStepWithRecovery()
    {
        var plan = CreatePlan(step: CreateStep(payloadJson: "{\"description\":\"Missing title\"}"));

        var result = new AiProposedPlanValidator().Validate(
            plan,
            CreateContext(halRels: HalSet("create-event")));

        await Assert.That(result.CanRequestConfirmation).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_tool_argument");
        await Assert.That(result.Steps.Single().Status).IsEqualTo(AiPlanStepStatus.RequiresClarification);
        await Assert.That(result.Steps.Single().FailureMessage).DoesNotContain("title");
    }

    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid ConversationId = Guid.CreateVersion7();
    private static readonly DateTime UtcNow = new(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);

    private static AiProposedPlan CreatePlan(AiProposedPlanStep? step = null)
        => new(TenantId, ConversationId, [step ?? CreateStep()]);

    private static AiProposedPlanStep CreateStep(
        string stepId = "step-1",
        string toolName = "CreateEventDraft",
        string payloadJson = "{\"title\":\"Community dinner\",\"participationConfiguration\":{\"participationHandlingModeId\":1,\"advanceRegistrationObligationId\":1}}",
        DateTime? capturedAtUtc = null,
        AiPlanStepStatus status = AiPlanStepStatus.Proposed,
        bool requiresClarification = false,
        string? clarificationQuestion = null)
        => new(
            stepId,
            AiProposedActionKind.CreateEventDraft,
            toolName,
            payloadJson,
            capturedAtUtc ?? UtcNow,
            status,
            RequiredHalLinkRels: null,
            requiresClarification,
            clarificationQuestion);

    private static AiPlanValidationContext CreateContext(
        IReadOnlySet<string>? halRels = null,
        TimeSpan? maxContextAge = null)
        => new(TenantId, halRels ?? HalSet(), UtcNow, maxContextAge ?? TimeSpan.FromMinutes(30));

    private static HashSet<string> HalSet(params string[] rels) => new(rels, StringComparer.OrdinalIgnoreCase);
}
