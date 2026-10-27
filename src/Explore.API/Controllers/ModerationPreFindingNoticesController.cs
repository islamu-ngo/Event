using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Responses;
using Explore.Application.Features.Moderation.Responses.Commands;
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
[Route("api/moderation/pre-finding/notices/{noticeId:guid}")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("authenticated")]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationPreFindingNoticesController(
    IQueryHandler<GetModerationPreFindingNoticeQuery, ModerationPreFindingNoticeResult> notice,
    ICommandHandler<AcknowledgeModerationPreFindingNoticeCommand, BaseCommandResponse<Guid>> acknowledge,
    ICommandHandler<SubmitModerationPreFindingResponseCommand, BaseCommandResponse<Guid>> respond,
    IOptions<JsonOptions> json) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid notice operation", "The protected notice intent is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .NotFound(new("Notice unavailable", "The protected pre-finding notice is unavailable."), FailureCodes.NotFound)
        .Conflict("Notice conflict", "The original notice or response receipt cannot be replaced.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Notice unavailable", "Current pre-finding custody is unavailable.", "ModerationPreFindingUnavailable");

    /// <summary>A private recipient or independently qualified reviewer view; GET never acknowledges.</summary>
    [AllowAnonymous]
    [HttpGet(Name = RouteNames.GetModerationPreFindingNotice)]
    [ProducesResponseType<HalResource<ModerationPreFindingView>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Get(Guid noticeId, [FromQuery] bool reviewResponse = false,
        CancellationToken cancellationToken = default) =>
        new ModerationPreFindingNoticeHttpResult(
            await notice.QueryAsync(new(noticeId, reviewResponse), cancellationToken),
            view => Resource(view, reviewResponse), json,
            outcome => Failures.Map(this, DisclosureFailure(outcome)));

    [Authorize]
    [EnableRateLimiting("write")]
    [SuppressIdempotencyResponseStorage]
    [Consumes("application/json")]
    [HttpPost("acknowledgement", Name = RouteNames.AcknowledgeModerationPreFindingNotice)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Acknowledge(Guid noticeId,
        [FromBody] AcknowledgeModerationPreFindingNoticeCommand command, CancellationToken cancellationToken)
    {
        var result = await acknowledge.ExecuteAsync(command with { NoticeId = noticeId }, cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }

    [Authorize]
    [EnableRateLimiting("write")]
    [SuppressIdempotencyResponseStorage]
    [Consumes("application/json")]
    [RequestSizeLimit(512 * 1024)]
    [HttpPost("response", Name = RouteNames.SubmitModerationPreFindingResponse)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Respond(Guid noticeId,
        [FromBody] SubmitModerationPreFindingResponseCommand command, CancellationToken cancellationToken)
    {
        var result = await respond.ExecuteAsync(command with { NoticeId = noticeId }, cancellationToken);
        return Failures.Map(this, result, () => Ok(result));
    }

    private HalResource<ModerationPreFindingView> Resource(ModerationPreFindingView view, bool reviewResponse)
    {
        var resource = new HalResource<ModerationPreFindingView>(view).WithSelfLink(
            Url.Link(RouteNames.GetModerationPreFindingNotice, new { noticeId = view.NoticeId, reviewResponse })
            ?? throw new InvalidOperationException("The pre-finding notice route is unavailable."));
        // This callback runs only inside the native owner's qualified recipient/reviewer custody.
        // A reviewer view never supplies recipient mutation affordances.
        if (!reviewResponse && view.RespondedAt is null)
        {
            if (view.AcknowledgedAt is null)
                resource.WithLink("acknowledge", HalLink.CreateAction(
                    Url.Link(RouteNames.AcknowledgeModerationPreFindingNotice, new { noticeId = view.NoticeId })
                    ?? throw new InvalidOperationException("The pre-finding acknowledgement route is unavailable."),
                    HttpMethods.Post));
            resource.WithLink("submit-response", HalLink.CreateAction(
                Url.Link(RouteNames.SubmitModerationPreFindingResponse, new { noticeId = view.NoticeId })
                ?? throw new InvalidOperationException("The pre-finding response route is unavailable."),
                HttpMethods.Post));
        }
        return resource;
    }

    private static BaseCommandResponse<Guid> DisclosureFailure(ModerationQualificationWriteOutcome outcome) =>
        outcome switch
        {
            ModerationQualificationWriteOutcome.Unauthenticated =>
                BaseCommandResponse.Authentication<Guid>(),
            ModerationQualificationWriteOutcome.NotFound or ModerationQualificationWriteOutcome.Denied =>
                BaseCommandResponse.NotFound<Guid>(),
            _ => BaseCommandResponse.Failure<Guid>("ModerationPreFindingUnavailable")
        };
}
