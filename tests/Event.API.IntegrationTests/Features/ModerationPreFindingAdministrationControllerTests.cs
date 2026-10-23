using Explore.API.Controllers;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Responses;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationPreFindingAdministrationControllerTests
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
    public async Task ValidBodyCannotReplaceInvalidRouteCase(int operation, bool wrongVersion)
    {
        var result = await ExecuteAsync(operation, wrongVersion ? Guid.NewGuid() : Guid.Empty,
            Guid.CreateVersion7(), new NativeRepository(true));
        await AssertValidationAsync(result);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ValidBodyCannotReplaceInvalidRouteCustody(bool withdraw, bool wrongVersion)
    {
        var result = await ExecuteAsync(withdraw ? 3 : 1, Guid.CreateVersion7(),
            wrongVersion ? Guid.NewGuid() : Guid.Empty, new NativeRepository(true));
        await AssertValidationAsync(result);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task UnavailableNativeAuthorityReturnsProblemDetailsNotSuccess(int operation)
    {
        var result = await ExecuteAsync(operation, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new NativeRepository(false));
        var failure = result.Result as ObjectResult;
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(failure.Value is ProblemDetails).IsTrue();
        await Assert.That(failure.Value is BaseCommandResponse<Guid>).IsFalse();
    }

    private static async Task AssertValidationAsync(ActionResult<BaseCommandResponse<Guid>> result)
    {
        var failure = result.Result as ObjectResult;
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(failure.Value is ValidationProblemDetails validation
            && validation.Errors.ContainsKey("request")).IsTrue();
    }

    private static async Task<ActionResult<BaseCommandResponse<Guid>>> ExecuteAsync(
        int operation, Guid caseId, Guid custodyId, IModerationPreFindingRepository repository)
    {
        var custody = new ModerationPreFindingCustodyController(
            new ProposeModerationPreFindingCustodyCommandHandler(repository),
            new AcceptModerationPreFindingCustodyCommandHandler(repository),
            new WithdrawModerationPreFindingCustodyCommandHandler(repository))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var proposal = new ProposeModerationPreFindingCustodyCommand
        {
            OperationId = Guid.CreateVersion7(), CaseId = Guid.CreateVersion7(), ExpectedCaseRevision = 1,
            PrivacyOwnerHumanId = Guid.CreateVersion7(),
            Terms = new(TimeSpan.FromHours(1), 32768, "Protected response opportunity", "Explicit case-purpose mandate"),
            ValidUntil = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero)
        };
        switch (operation)
        {
            case 0:
                return await custody.Propose(caseId, proposal, default);
            case 1:
                return await custody.Accept(caseId, custodyId, new()
                {
                    OperationId = proposal.OperationId, CaseId = proposal.CaseId, ExpectedCaseRevision = 1,
                    CustodyId = Guid.CreateVersion7(), ExpectedCustodyRevision = 1
                }, default);
            case 3:
                return await custody.Withdraw(caseId, custodyId, new()
                {
                    OperationId = proposal.OperationId, CaseId = proposal.CaseId, ExpectedCaseRevision = 1,
                    CustodyId = Guid.CreateVersion7(), ExpectedCustodyRevision = 2
                }, default);
            default:
                var notices = new ModerationPreFindingNoticeIssuanceController(
                    new IssueModerationPreFindingNoticeCommandHandler(repository))
                {
                    ControllerContext = new() { HttpContext = new DefaultHttpContext() }
                };
                return await notices.Issue(caseId, new()
                {
                    OperationId = proposal.OperationId, CaseId = proposal.CaseId, ExpectedCaseRevision = 1,
                    CustodyId = custodyId, Recipient = new(Guid.Empty, 0, Guid.CreateVersion7()),
                    Text = new(new("Subject-safe allegation"), new("Applicable rule"), new("Respond privately")),
                    RetainUntil = proposal.ValidUntil
                }, default);
        }
    }

    private sealed class NativeRepository(bool rejectInvalidSelector) : IModerationPreFindingRepository
    {
        public Task<ModerationPreFindingWriteResult> ExecutePreFindingAsync(
            ModerationPreFindingOperation operation, CancellationToken cancellationToken) =>
            rejectInvalidSelector
                ? throw new InvalidOperationException("Invalid route authority must not reach native pre-finding custody.")
                : Task.FromResult(new ModerationPreFindingWriteResult(ModerationQualificationWriteOutcome.Unavailable));

        public Task<ModerationQualificationWriteOutcome> DisclosePreFindingAsync(
            Guid tenantId, Guid noticeId, bool reviewResponse,
            Func<ModerationPreFindingView, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
            Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Administration must not disclose a private notice or response.");

        public Task<int> PurgeExpiredPreFindingAsync(int batchSize, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Administration must not invoke scheduled payload cleanup.");
    }
}
