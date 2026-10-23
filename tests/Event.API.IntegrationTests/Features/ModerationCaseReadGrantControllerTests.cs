using Explore.API.Controllers;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationCaseReadGrantControllerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GrantCannotReplaceAnInvalidRouteCaseWithAValidBodyCase(bool wrongVersion)
    {
        var command = new GrantModerationCaseReadPurposeCommand
        {
            OperationId = Guid.CreateVersion7(), CaseId = Guid.CreateVersion7(), ExpectedCaseRevision = 1,
            SourceId = Guid.CreateVersion7(), GranteeUserId = Guid.CreateVersion7(),
            ValidUntil = new(2026, 10, 12, 16, 0, 0, TimeSpan.Zero)
        };
        var response = await Controller().Grant(
            wrongVersion ? Guid.NewGuid() : Guid.Empty, command, default);
        var failure = response.Result as ObjectResult;
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(failure.Value is ValidationProblemDetails validation
            && validation.Errors.ContainsKey("request")).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RevocationCannotReplaceAnInvalidRouteGrantWithAValidBodyGrant(bool wrongVersion)
    {
        var command = new RevokeModerationCaseReadPurposeCommand
        {
            OperationId = Guid.CreateVersion7(), GrantId = Guid.CreateVersion7(), ExpectedGrantRevision = 1
        };
        var response = await Controller().Revoke(
            wrongVersion ? Guid.NewGuid() : Guid.Empty, command, default);
        var failure = response.Result as ObjectResult;
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(failure.Value is ValidationProblemDetails validation
            && validation.Errors.ContainsKey("request")).IsTrue();
    }

    private static ModerationCaseReadGrantController Controller()
    {
        var repository = new InvalidSelectorRepository();
        return new(new GrantModerationCaseReadPurposeCommandHandler(repository),
            new RevokeModerationCaseReadPurposeCommandHandler(repository))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
    }

    private sealed class InvalidSelectorRepository : IModerationCaseReadGrantRepository
    {
        public Task<ModerationCaseReadGrant?> GrantAsync(
            ModerationCaseReadGrantRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An invalid route selector must not reach native read-grant administration.");

        public Task<ModerationCaseReadGrant?> RevokeAsync(
            Guid grantId, Guid operationId, long expectedRevision, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An invalid route selector must not reach native read-grant revocation.");
    }
}
