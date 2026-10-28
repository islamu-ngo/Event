using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Features.Moderation.Inspection;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/moderation/resources/{resourceId:guid}")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(32 * 1024)]
[Produces("application/json", "application/problem+json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationResourceSafetyController(
    ICommandHandler<RecordModerationResourceSafetyReviewCommand, ModerationResourceSafetyResponse> review,
    ICommandHandler<ReleaseModerationResourceQuarantineCommand, ModerationResourceSafetyResponse> release,
    IQueryHandler<InspectModerationResourceQuarantineQuery, ModerationResourceQuarantineInspectionResult> inspection,
    IQueryHandler<ReadModerationResourceQuarantineStateQuery, ModerationResourceQuarantineStateResult> state,
    IQueryHandler<ReadModerationResourceQuarantineReviewStateQuery, ModerationResourceQuarantineReviewStateResult> reviewState)
    : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid resource safety request", "The resource safety request is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Resource safety forbidden", "Current independent native authority is required.", FailureCodes.AdminRequired)
        .NotFound(new("Resource safety unavailable", "The requested resource safety record is unavailable."), FailureCodes.NotFound)
        .Conflict("Resource safety conflict", "The original intent or generation does not match.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Resource safety unavailable", "Current native safety qualification is unavailable.", "ModerationAuthorityUnavailable");

    [HttpPost("quarantine-reviews", Name = RouteNames.RecordModerationResourceSafetyReview)]
    [SuppressIdempotencyResponseStorage]
    [ProducesResponseType(typeof(ModerationResourceSafetyResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationResourceSafetyResponse>> RecordReview(
        Guid resourceId, [FromBody] RecordModerationResourceSafetyReviewCommand command,
        CancellationToken cancellationToken)
    {
        var response = await review.ExecuteAsync(command with
        {
            Intent = command.Intent with { ResourceId = resourceId }
        }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    [HttpPost("quarantine-causes/{quarantineId:guid}/release", Name = RouteNames.ReleaseModerationResourceQuarantine)]
    [SuppressIdempotencyResponseStorage]
    [ProducesResponseType(typeof(ModerationResourceSafetyResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationResourceSafetyResponse>> ReleaseCause(
        Guid resourceId, Guid quarantineId, [FromBody] ReleaseModerationResourceQuarantineCommand command,
        CancellationToken cancellationToken)
    {
        var response = await release.ExecuteAsync(command with
        {
            Intent = command.Intent with { ResourceId = resourceId, QuarantineId = quarantineId }
        }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    [HttpPost("quarantine-causes/{quarantineId:guid}/inspection", Name = RouteNames.InspectModerationResourceQuarantine)]
    [SuppressIdempotencyResponseStorage]
    [Produces("application/octet-stream", "text/plain")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Inspect(
        Guid resourceId, Guid quarantineId, [FromBody] InspectModerationResourceQuarantineQuery query,
        CancellationToken cancellationToken)
    {
        var pending = await inspection.QueryAsync(query with
        {
            Intent = query.Intent with { ResourceId = resourceId, QuarantineId = quarantineId }
        }, cancellationToken);
        return new ModerationResourceQuarantineInspectionHttpResult(pending);
    }

    [HttpPost("quarantine-state", Name = RouteNames.ReadModerationResourceQuarantineState)]
    [SuppressIdempotencyResponseStorage]
    [Produces("application/hal+json")]
    [ProducesResponseType(typeof(Explore.Application.Hateoas.HalResource<ModerationResourceQuarantineStateHeader>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult> ReadState(
        Guid resourceId, [FromBody] ReadModerationResourceQuarantineStateQuery query,
        CancellationToken cancellationToken)
    {
        var pending = await state.QueryAsync(query with
        {
            Intent = query.Intent with { ResourceId = resourceId }
        }, cancellationToken);
        return new ModerationResourceQuarantineStateHttpResult(pending);
    }

    [HttpPost("quarantine-causes/{quarantineId:guid}/review-state", Name = RouteNames.ReadModerationResourceQuarantineReviewState)]
    [SuppressIdempotencyResponseStorage]
    [Produces("application/hal+json")]
    [ProducesResponseType(typeof(Explore.Application.Hateoas.HalResource<ModerationResourceQuarantineReviewState>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult> ReadReviewState(
        Guid resourceId, Guid quarantineId, [FromBody] ReadModerationResourceQuarantineReviewStateQuery query,
        CancellationToken cancellationToken)
    {
        var pending = await reviewState.QueryAsync(query with
        {
            Intent = query.Intent with { ResourceId = resourceId, QuarantineId = quarantineId }
        }, cancellationToken);
        return new ModerationResourceQuarantineReviewStateHttpResult(pending);
    }
}
