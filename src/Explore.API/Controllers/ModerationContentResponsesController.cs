using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Responses;
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
[Route("api/moderation/notices/{noticeId:guid}/content-response")]
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
public sealed class ModerationContentResponsesController(
    ICommandHandler<SubmitModerationContentResponseCommand, BaseCommandResponse<Guid>> submit,
    IQueryHandler<GetModerationContentResponseAvailabilityQuery, ModerationContentResponseAvailabilityResult> availability,
    IResourceAssembler<ModerationContentResponseAvailability, ModerationContentResponseAvailability> assembler,
    IOptions<JsonOptions> json) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid response", "The protected response coordinates or statement are invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .NotFound(new("Response unavailable", "The original Content response is unavailable."), FailureCodes.NotFound)
        .Conflict("Response conflict", "The original response receipt cannot be replaced.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Response unavailable", "Current protected response custody is unavailable.", "ModerationContentResponseUnavailable");

    /// <summary>Qualifies the current original representative's protected response affordance.</summary>
    [AllowAnonymous]
    [HttpGet(Name = RouteNames.GetModerationContentResponseAvailability)]
    [ProducesResponseType<HalResource<ModerationContentResponseAvailability>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Availability(Guid noticeId, CancellationToken cancellationToken) =>
        new ModerationContentResponseAvailabilityHttpResult(
            await availability.QueryAsync(new(noticeId), cancellationToken), assembler, json,
            outcome => Failures.Map(this, ModerationContentResponseAvailabilityResult.Failure(outcome)));

    /// <summary>Submits finite Content response text without acknowledging notice or changing a finding.</summary>
    [Authorize]
    [EnableRateLimiting("write")]
    [SuppressIdempotencyResponseStorage]
    [Consumes("application/json")]
    [RequestSizeLimit(64 * 1024)]
    [HttpPost(Name = RouteNames.SubmitModerationContentResponse)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Submit(Guid noticeId,
        [FromBody] SubmitModerationContentResponseCommand command, CancellationToken cancellationToken)
    {
        var result = await submit.ExecuteAsync(command with { NoticeId = noticeId }, cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }
}
