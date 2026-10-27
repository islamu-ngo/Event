using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Responses;
using Explore.Application.Features.Moderation.Responses.Commands;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/moderation/pre-finding/cases/{caseId:guid}/notices")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(128 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationPreFindingNoticeIssuanceController(
    ICommandHandler<IssueModerationPreFindingNoticeCommand, BaseCommandResponse<Guid>> issue) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid notice issuance", "The pre-finding notice intent is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .NotFound(new("Notice unavailable", "The selected pre-finding notice purpose is unavailable."), FailureCodes.NotFound)
        .Conflict("Notice conflict", "The original notice or operation receipt cannot be replaced.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Notice unavailable", "Current pre-finding notice authority is unavailable.", "ModerationPreFindingUnavailable");

    /// <summary>Issues independently authored subject-safe text under accepted finite custody; success returns only the notice ID.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost(Name = RouteNames.IssueModerationPreFindingNotice)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Issue(Guid caseId,
        [FromBody] IssueModerationPreFindingNoticeCommand command, CancellationToken cancellationToken)
    {
        var response = await issue.ExecuteAsync(command with { CaseId = caseId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
