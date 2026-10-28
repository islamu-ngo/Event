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
[Route("api/moderation/decisions/{decisionId:guid}")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(16 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationDecisionAdjudicationController(
    ICommandHandler<AcceptModerationFindingCommand, ModerationLifecycleResponse> acceptFinding,
    ICommandHandler<ApproveModerationDecisionCommand, ModerationLifecycleResponse> approve,
    ICommandHandler<RejectModerationDecisionCommand, ModerationLifecycleResponse> reject,
    ICommandHandler<WithdrawModerationDecisionCommand, ModerationLifecycleResponse> withdraw) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid adjudication intent", "The moderation adjudication intent is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Adjudication forbidden", "Current native adjudication authority is required.", FailureCodes.AdminRequired)
        .NotFound(new("Decision unavailable", "The selected moderation decision is unavailable."), FailureCodes.NotFound)
        .Conflict("Decision conflict", "The expected decision or policy revision is no longer current.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Adjudication unavailable", "Current moderation authority is unavailable.", "ModerationAuthorityUnavailable");

    /// <summary>Accepts one exact qualified finding without serializing its evidence or establishing authority from intent.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("findings/{findingId:guid}/acceptance", Name = RouteNames.AcceptModerationFinding)]
    [ProducesResponseType<ModerationLifecycleResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationLifecycleResponse>> AcceptFinding(Guid decisionId, Guid findingId,
        [FromBody] AcceptModerationFindingCommand command, CancellationToken cancellationToken)
    {
        var response = await acceptFinding.ExecuteAsync(command with { DecisionId = decisionId, FindingId = findingId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Appends the current independent human's approval under the native decision and reviewer fences.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("approval", Name = RouteNames.ApproveModerationDecision)]
    [ProducesResponseType<ModerationLifecycleResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationLifecycleResponse>> Approve(Guid decisionId,
        [FromBody] ApproveModerationDecisionCommand command, CancellationToken cancellationToken)
    {
        var response = await approve.ExecuteAsync(command with { DecisionId = decisionId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Rejects the selected decision through its native current-corrector authority.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("rejection", Name = RouteNames.RejectModerationDecision)]
    [ProducesResponseType<ModerationLifecycleResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationLifecycleResponse>> Reject(Guid decisionId,
        [FromBody] RejectModerationDecisionCommand command, CancellationToken cancellationToken)
    {
        var response = await reject.ExecuteAsync(command with { DecisionId = decisionId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Withdraws the selected decision through its original-author authority without reopening a closed case.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("withdrawal", Name = RouteNames.WithdrawModerationDecision)]
    [ProducesResponseType<ModerationLifecycleResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationLifecycleResponse>> Withdraw(Guid decisionId,
        [FromBody] WithdrawModerationDecisionCommand command, CancellationToken cancellationToken)
    {
        var response = await withdraw.ExecuteAsync(command with { DecisionId = decisionId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
