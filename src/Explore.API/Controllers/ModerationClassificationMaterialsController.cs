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
[Route("api/moderation/classification-materials")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(16 * 1024 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationClassificationMaterialsController(
    ICommandHandler<RecordModerationClassificationMaterialCommand, ModerationQualificationResponse> record,
    ICommandHandler<AcceptModerationClassificationMaterialCommand, ModerationQualificationResponse> accept,
    ICommandHandler<EndModerationClassificationMaterialCommand, ModerationQualificationResponse> end) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid material review intent", "The material review request is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Material review forbidden", "Current independent native human authority is required.", FailureCodes.AdminRequired)
        .NotFound(new("Material review unavailable", "The requested native material review is unavailable."), FailureCodes.NotFound)
        .Conflict("Material review conflict", "The original intent or generation does not match.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Material review unavailable", "Current native qualification is unavailable.", "ModerationAuthorityUnavailable");

    /// <summary>Records exact candidate classifications only after native ownership and complete inventory acquisition.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("sources", Name = RouteNames.RecordModerationClassificationMaterial)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Record(
        [FromBody] RecordModerationClassificationMaterialCommand command, CancellationToken cancellationToken)
    {
        var response = await record.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Requires the nominated independent reviewer to accept the exact current whole-Event inventory.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("reviews", Name = RouteNames.AcceptModerationClassificationMaterial)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Accept(
        [FromBody] AcceptModerationClassificationMaterialCommand command, CancellationToken cancellationToken)
    {
        var response = await accept.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Ends the route-bound review under current correction authority without renewing original acceptance.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("reviews/{reviewId:guid}/end", Name = RouteNames.EndModerationClassificationMaterial)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> End(Guid reviewId,
        [FromBody] EndModerationClassificationMaterialCommand command, CancellationToken cancellationToken)
    {
        var response = await end.ExecuteAsync(command with { ReviewId = reviewId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
