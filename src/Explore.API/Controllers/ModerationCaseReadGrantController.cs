using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/moderation/case-read-grants")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(16 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationCaseReadGrantController(
    ICommandHandler<GrantModerationCaseReadPurposeCommand, BaseCommandResponse<Guid>> grant,
    ICommandHandler<RevokeModerationCaseReadPurposeCommand, BaseCommandResponse<Guid>> revoke) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid read-grant request", "The read-grant request is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Read-grant administration forbidden", "Current native human authority is required.",
            FailureCodes.AdminRequired)
        .Unavailable("Read-grant administration unavailable", "Current native read-grant authority is unavailable.",
            "ModerationCaseReadGrantUnavailable");

    /// <summary>Commits a finite case/material-specific read purpose without returning confidential material.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("{caseId:guid}", Name = RouteNames.GrantModerationCaseReadPurpose)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Grant(Guid caseId,
        [FromBody] GrantModerationCaseReadPurposeCommand command, CancellationToken cancellationToken)
    {
        var response = await grant.ExecuteAsync(command with { CaseId = caseId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Revokes one exact grant under current administrative authority and immutable operation ownership.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("{grantId:guid}/revoke", Name = RouteNames.RevokeModerationCaseReadPurpose)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Revoke(Guid grantId,
        [FromBody] RevokeModerationCaseReadPurposeCommand command, CancellationToken cancellationToken)
    {
        var response = await revoke.ExecuteAsync(command with { GrantId = grantId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
