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
[Route("api/moderation/decisions/{decisionId:guid}/effects/{effectId:guid}/notification-status")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("authenticated")]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationNoticeStatusController(
    IQueryHandler<GetModerationNoticeStatusQuery, ModerationNoticeStatusResult> read,
    IResourceAssembler<ModerationNoticeStatusDto, ModerationNoticeStatusDto> assembler,
    IOptions<JsonOptions> json) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid status request", "The status coordinates are invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .NotFound(new("Status unavailable", "The requested notification status is unavailable."), FailureCodes.NotFound)
        .Unavailable("Status unavailable", "Current native status qualification is unavailable.", "ModerationNoticeUnavailable");

    /// <summary>Reads minimal original notification proof, not complaint eligibility or private notice text.</summary>
    [AllowAnonymous]
    [HttpGet(Name = RouteNames.GetModerationNoticeStatus)]
    [ProducesResponseType<HalResource<ModerationNoticeStatusDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Read(Guid decisionId, Guid effectId, CancellationToken cancellationToken) =>
        new ModerationNoticeStatusHttpResult(await read.QueryAsync(new(decisionId, effectId), cancellationToken),
            assembler, json, outcome => Failures.Map(this, ModerationNoticeReadResult.Failure(outcome)));
}
