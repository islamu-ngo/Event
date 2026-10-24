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
[Route("api/moderation/adjudicated-case-read-grants")]
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
public sealed class ModerationAdjudicatedCaseReadGrantController(
    ICommandHandler<GrantModerationAdjudicatedCaseReadPurposeCommand, BaseCommandResponse<Guid>> grant,
    ICommandHandler<RevokeModerationAdjudicatedCaseReadPurposeCommand, BaseCommandResponse<Guid>> revoke) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid adjudicated read-grant request", "The adjudicated read-grant request is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Adjudicated read-grant administration forbidden", "Current native human authority is required.",
            FailureCodes.AdminRequired)
        .Unavailable("Adjudicated read-grant administration unavailable", "Current native read-grant authority is unavailable.",
            "moderation_adjudicated_case_read_grant_unavailable");

    /// <summary>Commits finite adjudicated case access without returning confidential material or minting a receipt.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("{caseId:guid}", Name = RouteNames.GrantModerationAdjudicatedCaseReadPurpose)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Grant(Guid caseId,
        [FromBody] GrantModerationAdjudicatedCaseReadPurposeCommand command, CancellationToken cancellationToken)
    {
        var response = await grant.ExecuteAsync(command with { CaseId = caseId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Revokes the exact live grant under independently verified current native administrative authority.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("{grantId:guid}/revoke", Name = RouteNames.RevokeModerationAdjudicatedCaseReadPurpose)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Revoke(Guid grantId,
        [FromBody] RevokeModerationAdjudicatedCaseReadPurposeCommand command, CancellationToken cancellationToken)
    {
        var response = await revoke.ExecuteAsync(command with { GrantId = grantId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
