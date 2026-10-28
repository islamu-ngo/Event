using Explore.API.Controllers;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationDecisionControllerTests
{
    [Test]
    [Arguments(0, false)]
    [Arguments(0, true)]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(2, false)]
    [Arguments(2, true)]
    [Arguments(3, false)]
    [Arguments(3, true)]
    [Arguments(4, false)]
    [Arguments(4, true)]
    [Arguments(5, false)]
    [Arguments(5, true)]
    [Arguments(6, false)]
    [Arguments(6, true)]
    [Arguments(7, false)]
    [Arguments(7, true)]
    public async Task ValidBodyCannotReplaceInvalidRouteDecision(int action, bool wrongVersion)
    {
        var result = await ExecuteAsync(action, wrongVersion ? Guid.NewGuid() : Guid.Empty,
            Guid.CreateVersion7(), -1);
        await Assert.That((result.Result as ObjectResult)?.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That((result.Result as ObjectResult)?.Value is ValidationProblemDetails).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FindingAcceptanceCannotBorrowValidBodyFinding(bool wrongVersion)
    {
        var result = await ExecuteAsync(0, Guid.CreateVersion7(),
            wrongVersion ? Guid.NewGuid() : Guid.Empty, -1);
        await Assert.That((result.Result as ObjectResult)?.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
    }

    [Test]
    [Arguments(6, false)]
    [Arguments(6, true)]
    [Arguments(7, false)]
    [Arguments(7, true)]
    public async Task CorrectionCannotBorrowValidBodySanctionSelector(int action, bool wrongVersion)
    {
        var result = await ExecuteAsync(action, Guid.CreateVersion7(),
            wrongVersion ? Guid.NewGuid() : Guid.Empty, -1);
        await Assert.That((result.Result as ObjectResult)?.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
    }

    [Test]
    [Arguments(0, 0, StatusCodes.Status403Forbidden)]
    [Arguments(1, 0, StatusCodes.Status403Forbidden)]
    [Arguments(2, 0, StatusCodes.Status403Forbidden)]
    [Arguments(3, 0, StatusCodes.Status403Forbidden)]
    [Arguments(4, 0, StatusCodes.Status403Forbidden)]
    [Arguments(5, 0, StatusCodes.Status403Forbidden)]
    [Arguments(6, 0, StatusCodes.Status403Forbidden)]
    [Arguments(7, 0, StatusCodes.Status403Forbidden)]
    [Arguments(0, 1, StatusCodes.Status503ServiceUnavailable)]
    [Arguments(1, 1, StatusCodes.Status503ServiceUnavailable)]
    [Arguments(2, 1, StatusCodes.Status503ServiceUnavailable)]
    [Arguments(3, 1, StatusCodes.Status503ServiceUnavailable)]
    [Arguments(4, 1, StatusCodes.Status503ServiceUnavailable)]
    [Arguments(5, 1, StatusCodes.Status503ServiceUnavailable)]
    [Arguments(6, 1, StatusCodes.Status503ServiceUnavailable)]
    [Arguments(7, 1, StatusCodes.Status503ServiceUnavailable)]
    [Arguments(0, 2, StatusCodes.Status409Conflict)]
    [Arguments(1, 2, StatusCodes.Status409Conflict)]
    [Arguments(2, 2, StatusCodes.Status409Conflict)]
    [Arguments(3, 2, StatusCodes.Status409Conflict)]
    [Arguments(4, 2, StatusCodes.Status409Conflict)]
    [Arguments(5, 2, StatusCodes.Status409Conflict)]
    [Arguments(6, 2, StatusCodes.Status409Conflict)]
    [Arguments(7, 2, StatusCodes.Status409Conflict)]
    [Arguments(0, 3, StatusCodes.Status404NotFound)]
    [Arguments(1, 3, StatusCodes.Status404NotFound)]
    [Arguments(2, 3, StatusCodes.Status404NotFound)]
    [Arguments(3, 3, StatusCodes.Status404NotFound)]
    [Arguments(4, 3, StatusCodes.Status404NotFound)]
    [Arguments(5, 3, StatusCodes.Status404NotFound)]
    [Arguments(6, 3, StatusCodes.Status404NotFound)]
    [Arguments(7, 3, StatusCodes.Status404NotFound)]
    public async Task ActualServiceFailuresRemainDistinctProblemDetails(int action, int authority, int status)
    {
        var result = await ExecuteAsync(action, Guid.CreateVersion7(), Guid.CreateVersion7(), authority);
        var failure = result.Result as ObjectResult;
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.StatusCode).IsEqualTo(status);
        await Assert.That(failure.Value is ProblemDetails).IsTrue();
        await Assert.That(failure.Value is ModerationLifecycleResponse).IsFalse();
    }

    private static async Task<ActionResult<ModerationLifecycleResponse>> ExecuteAsync(
        int action, Guid decisionId, Guid findingId, int authority)
    {
        var operations = new ModerationOperationService(new NativeStore(authority < 0),
            new NativeAuthority(authority), new UnexpectedEffectPlanner(), TimeProvider.System);
        var adjudication = new ModerationDecisionAdjudicationController(
            new AcceptModerationFindingCommandHandler(operations), new ApproveModerationDecisionCommandHandler(operations),
            new RejectModerationDecisionCommandHandler(operations), new WithdrawModerationDecisionCommandHandler(operations))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var target = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), target);
        var operation = Guid.CreateVersion7();
        var effect = Guid.CreateVersion7();
        var bodyDecision = Guid.CreateVersion7();
        switch (action)
        {
            case 0:
                return await adjudication.AcceptFinding(decisionId, findingId, new()
                {
                    OperationId = operation,
                    EffectId = effect,
                    DecisionId = bodyDecision,
                    Subject = subject,
                    Target = target,
                    ExpectedRevision = 1,
                    ExpectedPolicyRevision = 1,
                    FindingId = Guid.CreateVersion7()
                }, default);
            case 1:
                return await adjudication.Approve(decisionId, new()
                {
                    OperationId = operation,
                    EffectId = effect,
                    DecisionId = bodyDecision,
                    Subject = subject,
                    Target = target,
                    ExpectedRevision = 1,
                    ExpectedPolicyRevision = 1
                }, default);
            case 2:
                return await adjudication.Reject(decisionId, new()
                {
                    OperationId = operation,
                    EffectId = effect,
                    DecisionId = bodyDecision,
                    Subject = subject,
                    Target = target,
                    ExpectedRevision = 1,
                    ExpectedPolicyRevision = 1
                }, default);
            case 3:
                return await adjudication.Withdraw(decisionId, new()
                {
                    OperationId = operation,
                    EffectId = effect,
                    DecisionId = bodyDecision,
                    Subject = subject,
                    Target = target,
                    ExpectedRevision = 1,
                    ExpectedPolicyRevision = 1
                }, default);
            case 5:
                var proposals = new ModerationDecisionProposalController(new ProposeModerationDecisionCommandHandler(operations))
                {
                    ControllerContext = new() { HttpContext = new DefaultHttpContext() }
                };
                return await proposals.Propose(decisionId, new()
                {
                    OperationId = operation,
                    EffectId = effect,
                    DecisionId = bodyDecision,
                    Subject = subject,
                    Target = target,
                    ExpectedRevision = 1,
                    ExpectedPolicyRevision = 1,
                    Proposal = new(Guid.CreateVersion7(), ModerationRuleReference.Create(Guid.CreateVersion7(), "conduct", 1),
                        [ModerationCapability.Publish], new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero),
                        new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero), null, 1)
                }, default);
            case 6:
                var revocation = new ModerationSanctionRevocationController(new RevokeModerationSanctionCommandHandler(operations))
                {
                    ControllerContext = new() { HttpContext = new DefaultHttpContext() }
                };
                return await revocation.Revoke(decisionId, findingId, new()
                {
                    OperationId = operation,
                    EffectId = effect,
                    DecisionId = bodyDecision,
                    Subject = subject,
                    Target = target,
                    ExpectedRevision = 1,
                    ExpectedPolicyRevision = 1,
                    SanctionId = Guid.CreateVersion7()
                }, default);
            case 7:
                var supersession = new ModerationSanctionSupersessionController(new SupersedeModerationSanctionCommandHandler(operations))
                {
                    ControllerContext = new() { HttpContext = new DefaultHttpContext() }
                };
                return await supersession.Supersede(decisionId, findingId, new()
                {
                    OperationId = operation,
                    EffectId = effect,
                    DecisionId = bodyDecision,
                    Subject = subject,
                    Target = target,
                    ExpectedRevision = 1,
                    ExpectedPolicyRevision = 1,
                    SanctionId = Guid.CreateVersion7(),
                    SupersededSanctionId = Guid.CreateVersion7(),
                    SupersededScope = target,
                    ExpectedSanctionRevision = 1
                }, default);
            default:
                var activation = new ModerationDecisionActivationController(new ActivateModerationDecisionCommandHandler(operations))
                {
                    ControllerContext = new() { HttpContext = new DefaultHttpContext() }
                };
                return await activation.Activate(decisionId, new()
                {
                    OperationId = operation,
                    EffectId = effect,
                    DecisionId = bodyDecision,
                    Subject = subject,
                    Target = target,
                    ExpectedRevision = 1,
                    ExpectedPolicyRevision = 1,
                    SanctionId = Guid.CreateVersion7()
                }, default);
        }
    }

    private sealed class NativeStore(bool rejectInvalidIntent) : IModerationStore
    {
        public Task<ModerationOperationResult> ExecuteAsync(ModerationOperation operation,
            Func<IModerationSession, CancellationToken, Task<ModerationOperationResult>> execute, CancellationToken token) =>
            rejectInvalidIntent ? throw new InvalidOperationException("Invalid route intent reached the native store.")
                : execute(Substitute.For<IModerationSession>(), token);
    }

    private sealed class NativeAuthority(int state) : IModerationAuthorityContext
    {
        public Task<ModerationAuthorityResolution> ResolveAsync(
            IModerationSession session, ModerationOperation operation, CancellationToken token) => Task.FromResult(state switch
            {
                0 => ModerationAuthorityResolution.Denied(),
                1 => ModerationAuthorityResolution.Unavailable(),
                _ => ModerationAuthorityResolution.Known(new(
                    SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), operation.Target),
                    operation.Subject, operation.Target, operation.Kind, state == 2 ? 2 : 1, 1, ModerationAuthorityState.Known))
            });

        public Task<ModerationAuthorityState> QualifyActivationAsync(IModerationSession session,
            ModerationDecision decision, ModerationAuthorityGrant grant, CancellationToken token) =>
            throw new InvalidOperationException("Unavailable or missing decision authority must not qualify activation.");
    }

    private sealed class UnexpectedEffectPlanner : IModerationEffectPlanner
    {
        public ModerationPlannedEffect Plan(ModerationOperation operation, ModerationDecision decision,
            ModerationSanction? sanction, ModerationSanction? supersededSanction) =>
            throw new InvalidOperationException("A denied, unavailable, conflicting or absent decision must not stage effects.");
    }
}
