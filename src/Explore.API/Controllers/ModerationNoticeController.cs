using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Notices;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/moderation/notices")]
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
public sealed class ModerationNoticeController(
    IQueryHandler<GetModerationNoticesQuery, ModerationNoticeReadResult> list,
    IQueryHandler<GetModerationNoticeQuery, ModerationNoticeReadResult> read,
    ICommandHandler<AcknowledgeModerationNoticeCommand, BaseCommandResponse<Guid>> acknowledge,
    IResourceAssembler<ModerationNoticeDto, ModerationNoticeDto> assembler,
    IOptions<JsonOptions> json, LinkGenerator routes) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid notice request", "The notice read coordinates are invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .NotFound(new("Notice unavailable", "The requested notice is unavailable."), FailureCodes.NotFound)
        .Conflict("Notice acknowledgement conflict", "The original notification proof cannot be replaced.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Notice unavailable", "Current native notice qualification is unavailable.", "ModerationNoticeUnavailable");

    /// <summary>Reads only the current canonical recipient's protected original explanations.</summary>
    [AllowAnonymous]
    [HttpGet(Name = RouteNames.GetModerationNotices)]
    [ProducesResponseType<HalCollectionResource<ModerationNoticeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List([FromQuery] Guid? afterId = null, [FromQuery] int limit = 25,
        CancellationToken cancellationToken = default) =>
        new ModerationNoticeResult(await list.QueryAsync(new(afterId, limit), cancellationToken),
            assembler, json, routes, false, limit, new { afterId, limit },
            outcome => Failures.Map(this, ModerationNoticeReadResult.Failure(outcome)));

    /// <summary>Opens an exact moderation-notice pointer without granting account or case mutation authority.</summary>
    [AllowAnonymous]
    [HttpGet("{id:guid}", Name = RouteNames.GetModerationNotice)]
    [ProducesResponseType<HalResource<ModerationNoticeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Read(Guid id, CancellationToken cancellationToken) =>
        new ModerationNoticeResult(await read.QueryAsync(new(id), cancellationToken),
            assembler, json, routes, true, 1, null,
            outcome => Failures.Map(this, ModerationNoticeReadResult.Failure(outcome)));

    /// <summary>Records this subject's first explicit acknowledgement of the exact original explanation.</summary>
    [Authorize]
    [EnableRateLimiting("write")]
    [SuppressIdempotencyResponseStorage]
    [HttpPost("{id:guid}/acknowledge", Name = RouteNames.AcknowledgeModerationNotice)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Acknowledge(Guid id,
        [FromBody] AcknowledgeModerationNoticeCommand command, CancellationToken cancellationToken)
    {
        var result = await acknowledge.ExecuteAsync(command with { Id = id }, cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }
}
