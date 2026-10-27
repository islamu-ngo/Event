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
[Route("api/moderation/classification-controls")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(1024 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationClassificationControlsController(
    ICommandHandler<RecordModerationClassificationControlSelectionCommand, ModerationQualificationResponse> record,
    ICommandHandler<AdoptModerationClassificationEventTargetCommand, ModerationQualificationResponse> adopt,
    ICommandHandler<RevokeModerationClassificationEventTargetCommand, ModerationQualificationResponse> revoke) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid classification control", "The classification control request is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Classification control forbidden", "Current independent native human authority is required.", FailureCodes.AdminRequired)
        .NotFound(new("Classification control unavailable", "The requested native classification control is unavailable."), FailureCodes.NotFound)
        .Conflict("Classification control conflict", "The original intent or generation does not match.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Classification control unavailable", "Current native qualification is unavailable.", "ModerationAuthorityUnavailable");

    /// <summary>Retains the complete CFG29 composition from an independently accepted whole-Event material review.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("selections", Name = RouteNames.RecordModerationClassificationControlSelection)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Record(
        [FromBody] RecordModerationClassificationControlSelectionCommand command, CancellationToken cancellationToken)
    {
        var response = await record.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Requires an independent administrator to adopt one exact retained Event binding ordinal.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("adoptions", Name = RouteNames.AdoptModerationClassificationEventTarget)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Adopt(
        [FromBody] AdoptModerationClassificationEventTargetCommand command, CancellationToken cancellationToken)
    {
        var response = await adopt.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Revokes the route-bound adoption using immutable birth provenance even after material withdrawal.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("adoptions/{adoptionId:guid}/revoke", Name = RouteNames.RevokeModerationClassificationEventTarget)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Revoke(Guid adoptionId,
        [FromBody] RevokeModerationClassificationEventTargetCommand command, CancellationToken cancellationToken)
    {
        var response = await revoke.ExecuteAsync(command with { AdoptionId = adoptionId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
