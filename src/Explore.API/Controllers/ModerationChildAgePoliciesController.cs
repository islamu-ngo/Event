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
[Route("api/moderation/child-age-policies")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(256 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationChildAgePoliciesController(
    ICommandHandler<RecordModerationChildAgePolicyCommand, ModerationQualificationResponse> record,
    ICommandHandler<AdoptModerationChildAgePolicyCommand, ModerationQualificationResponse> adopt,
    ICommandHandler<WithdrawModerationChildAgePolicyCommand, ModerationQualificationResponse> withdraw) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid child-age governance", "The child-age policy request is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Child-age governance forbidden", "Current independent native human authority is required.", FailureCodes.AdminRequired)
        .NotFound(new("Child-age policy unavailable", "The requested native policy is unavailable."), FailureCodes.NotFound)
        .Conflict("Child-age policy conflict", "The original intent or generation does not match.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Child-age policy unavailable", "Current native qualification is unavailable.", "ModerationAuthorityUnavailable");

    /// <summary>Records reviewed policy obligations without accepting individual data or granting collection permission.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("sources", Name = RouteNames.RecordModerationChildAgePolicy)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Record(
        [FromBody] RecordModerationChildAgePolicyCommand command, CancellationToken cancellationToken)
    {
        var response = await record.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Requires separately qualified privacy/legal and safeguarding review of the exact native source.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("adoptions", Name = RouteNames.AdoptModerationChildAgePolicy)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Adopt(
        [FromBody] AdoptModerationChildAgePolicyCommand command, CancellationToken cancellationToken)
    {
        var response = await adopt.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Withdraws the route-bound adoption without renewing original policy or individual collection authority.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("adoptions/{adoptionId:guid}/withdraw", Name = RouteNames.WithdrawModerationChildAgePolicy)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Withdraw(Guid adoptionId,
        [FromBody] WithdrawModerationChildAgePolicyCommand command, CancellationToken cancellationToken)
    {
        var response = await withdraw.ExecuteAsync(command with { AdoptionId = adoptionId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
