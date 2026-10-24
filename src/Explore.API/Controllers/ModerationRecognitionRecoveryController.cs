using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.RecognitionRecovery;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/moderation/recognition-recovery")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("authenticated")]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationRecognitionRecoveryController(
    IQueryHandler<GetRecognitionRecoveryEscalationsQuery, RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionRecoveryEscalationDto>>> list,
    IQueryHandler<GetRecognitionRecoveryEscalationQuery, RecognitionRecoveryReadResponse<RecognitionRecoveryEscalationDto>> read,
    IQueryHandler<GetRecognitionRecoveryContextQuery, RecognitionRecoveryReadResponse<RecognitionRecoveryContextDto>> context,
    ICommandHandler<AssessRecognitionRecoveryCommand, BaseCommandResponse<Guid>> assess,
    ICommandHandler<AuthorizeRecognitionRecoveryCommand, BaseCommandResponse<Guid>> authorize,
    ICommandHandler<AcknowledgeRecognitionRecoveryCommand, BaseCommandResponse<Guid>> acknowledge,
    IResourceAssembler<RecognitionRecoveryEscalationDto, RecognitionRecoveryEscalationDto> escalations,
    IResourceAssembler<RecognitionRecoveryContextDto, RecognitionRecoveryContextDto> contexts) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid recognition recovery request", "The recovery request is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Recognition recovery forbidden", "Current independent source ownership is required.", FailureCodes.AdminRequired)
        .NotFound(new("Recognition recovery unavailable", "The requested owner capability is unavailable."), FailureCodes.NotFound)
        .Conflict("Recognition recovery conflict", "The exact current recovery state does not permit this operation.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Recognition recovery unavailable", "Current native recovery qualification is unavailable.", "RecognitionRecoveryUnavailable");

    /// <summary>Lists only currently qualified operational notices for the authenticated named recipient.</summary>
    [AllowAnonymous]
    [HttpGet("escalations", Name = RouteNames.GetRecognitionRecoveryEscalations)]
    [ProducesResponseType<HalCollectionResource<RecognitionRecoveryEscalationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HalCollectionResource<RecognitionRecoveryEscalationDto>>> List(
        [FromQuery] Guid? afterId = null, [FromQuery] int limit = 25, CancellationToken cancellationToken = default)
    {
        var result = await list.QueryAsync(new(afterId, limit), cancellationToken);
        if (!result.IsSuccess) return Failures.Map(this, result);
        return Ok(await escalations.ToCollectionResource(result.Data!, RouteNames.GetRecognitionRecoveryEscalations,
            new { afterId, limit }, HttpContext));
    }

    /// <summary>Reads an exact recipient notice without publishing private recognition material.</summary>
    [AllowAnonymous]
    [HttpGet("escalations/{id:guid}", Name = RouteNames.GetRecognitionRecoveryEscalation)]
    [ProducesResponseType<HalResource<RecognitionRecoveryEscalationDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<RecognitionRecoveryEscalationDto>>> Read(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await read.QueryAsync(new(id), cancellationToken);
        if (!result.IsSuccess) return Failures.Map(this, result);
        return Ok(await escalations.ToResource(result.Data!, HttpContext));
    }

    /// <summary>Atomically qualifies the recipient's source and reads current bounded assessment coordinates.</summary>
    [AllowAnonymous]
    [HttpGet("escalations/{id:guid}/context", Name = RouteNames.GetRecognitionRecoveryContext)]
    [ProducesResponseType<HalResource<RecognitionRecoveryContextDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<RecognitionRecoveryContextDto>>> Context(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await context.QueryAsync(new(id), cancellationToken);
        if (!result.IsSuccess) return Failures.Map(this, result);
        return Ok(await contexts.ToResource(result.Data!, HttpContext));
    }

    /// <summary>The native named recovery owner assesses only this exact source and current request revision.</summary>
    [Authorize]
    [EnableRateLimiting("write")]
    [SuppressIdempotencyResponseStorage]
    [HttpPost("requests/{intentId:guid}/sources/{sourceId:guid}/assessments", Name = RouteNames.AssessRecognitionRecovery)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Assess(Guid intentId, Guid sourceId,
        [FromBody] AssessRecognitionRecoveryCommand command, CancellationToken cancellationToken)
    {
        var result = await assess.ExecuteAsync(command with { IntentId = intentId, SourceId = sourceId }, cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }

    /// <summary>The independent native privacy owner authorizes the frozen assessment; this does not execute erasure.</summary>
    [Authorize]
    [EnableRateLimiting("write")]
    [SuppressIdempotencyResponseStorage]
    [HttpPost("assessments/{id:guid}/authorize", Name = RouteNames.AuthorizeRecognitionRecovery)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Authorize(Guid id,
        [FromBody] AuthorizeRecognitionRecoveryCommand command, CancellationToken cancellationToken)
    {
        var result = await authorize.ExecuteAsync(command with { Id = id }, cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }

    /// <summary>Acknowledges only an actual committed operational delivery to this exact recipient.</summary>
    [Authorize]
    [EnableRateLimiting("write")]
    [SuppressIdempotencyResponseStorage]
    [HttpPost("escalations/{id:guid}/acknowledge", Name = RouteNames.AcknowledgeRecognitionRecovery)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Acknowledge(Guid id, CancellationToken cancellationToken)
    {
        var result = await acknowledge.ExecuteAsync(new(id), cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }
}
