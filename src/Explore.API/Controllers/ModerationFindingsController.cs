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
[Route("api/moderation")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(64 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationFindingsController(
    ICommandHandler<FinalizeModerationFindingCommand, ModerationQualificationResponse> finalize,
    ICommandHandler<ReverseModerationFindingCommand, ModerationQualificationResponse> reverse) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid finding operation", "The finding request is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Finding operation forbidden", "Current independent native human authority is required.", FailureCodes.AdminRequired)
        .NotFound(new("Finding unavailable", "The requested native case or finding is unavailable."), FailureCodes.NotFound)
        .Conflict("Finding operation conflict", "The original intent or expected revision does not match.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Finding operation unavailable", "Current native qualification is unavailable.", "ModerationAuthorityUnavailable");

    /// <summary>Finalizes the route-bound case finding through current independent review and protected-response qualification.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("cases/{caseId:guid}/findings/{findingId:guid}/finalize", Name = RouteNames.FinalizeModerationFinding)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> FinalizeFinding(Guid caseId, Guid findingId,
        [FromBody] FinalizeModerationFindingCommand command, CancellationToken cancellationToken)
    {
        var response = await finalize.ExecuteAsync(command with { CaseId = caseId, FindingId = findingId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Independently reverses the route-bound finding while preserving its original adjudication provenance.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("findings/{findingId:guid}/reverse", Name = RouteNames.ReverseModerationFinding)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> ReverseFinding(Guid findingId,
        [FromBody] ReverseModerationFindingCommand command, CancellationToken cancellationToken)
    {
        var response = await reverse.ExecuteAsync(command with { FindingId = findingId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
