using Explore.API.Controllers;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationCaseAttributionControllerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RenewalCannotReplaceAnInvalidRouteCaseWithAValidBodyCase(bool wrongVersion)
    {
        var controller = Controller();
        var request = new RenewModerationCaseAttributionCommand
        {
            CaseId = Guid.CreateVersion7(),
            ExpectedCaseRevision = 1,
            ExpectedSourceId = Guid.CreateVersion7(),
            ExpectedGeneration = 1,
            ExpectedHeadRevision = 1,
            Qualification = Qualification()
        };
        var result = await controller.Renew(
            wrongVersion ? Guid.NewGuid() : Guid.Empty, request, default);
        var failure = result.Result as ObjectResult;
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(failure.Value is ValidationProblemDetails validation
            && validation.Errors.ContainsKey("request")).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WithdrawalCannotReplaceAnInvalidRouteSourceWithAValidBodySource(bool wrongVersion)
    {
        var controller = Controller();
        var request = new WithdrawModerationCaseAttributionCommand
        {
            SourceId = Guid.CreateVersion7(),
            OperationId = Guid.CreateVersion7(),
            ExpectedSourceRevision = 1
        };
        var result = await controller.Withdraw(
            wrongVersion ? Guid.NewGuid() : Guid.Empty, request, default);
        var failure = result.Result as ObjectResult;
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(failure.Value is ValidationProblemDetails validation
            && validation.Errors.ContainsKey("request")).IsTrue();
    }

    private static ModerationCaseAttributionController Controller()
    {
        var repository = new InvalidSelectorRepository();
        return new(
            new AttributeModerationCaseCommandHandler(repository),
            new RenewModerationCaseAttributionCommandHandler(repository),
            new WithdrawModerationCaseAttributionCommandHandler(repository))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
    }

    private static AttributeModerationCaseCommand Qualification() => new()
    {
        OperationId = Guid.CreateVersion7(),
        OccurrenceCaseId = Guid.CreateVersion7(),
        ExpectedCaseRevision = 1,
        EvidenceCaseRevision = 1,
        ReadGrantId = Guid.CreateVersion7(),
        Basis = ModerationResponsibilityBasis.EventContent,
        SubjectKind = ModerationSubjectKind.Content,
        SubjectId = Guid.CreateVersion7(),
        ValidUntil = new(2026, 10, 12, 14, 0, 0, TimeSpan.Zero)
    };

    private sealed class InvalidSelectorRepository : IModerationCaseAttributionRepository
    {
        public Task<ModerationCase?> AttributeAsync(
            ModerationCaseAttributionRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An invalid transport selector must not reach native attribution.");

        public Task<ModerationCaseAttributionSource?> RenewAsync(
            ModerationCaseAttributionRenewalRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An invalid route selector must not reach native renewal.");

        public Task<ModerationCaseAttributionSource?> WithdrawAsync(
            Guid sourceId, Guid operationId, long expectedRevision, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An invalid route selector must not reach native withdrawal.");
    }
}
