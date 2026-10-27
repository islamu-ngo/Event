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
[Route("api/moderation/case-narrative-policies")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(32 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationCaseNarrativePoliciesController(
    ICommandHandler<RecordModerationCaseNarrativePolicyCommand, ModerationQualificationResponse> record,
    ICommandHandler<AdoptModerationCaseNarrativePolicyCommand, ModerationQualificationResponse> adopt,
    ICommandHandler<WithdrawModerationCaseNarrativePolicyCommand, ModerationQualificationResponse> withdraw) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid custody policy request", "The custody policy request is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Custody policy forbidden", "Current native human authority is required.", FailureCodes.AdminRequired)
        .NotFound(new("Custody policy unavailable", "The requested custody policy is unavailable."), FailureCodes.NotFound)
        .Conflict("Custody policy conflict", "The original intent or generation does not match.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Custody policy unavailable", "Current native qualification is unavailable.", "ModerationAuthorityUnavailable");

    /// <summary>Records independently accountable finite terms without adopting them or authorizing a restriction.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("sources", Name = RouteNames.RecordModerationCaseNarrativePolicy)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Record(
        [FromBody] RecordModerationCaseNarrativePolicyCommand command, CancellationToken cancellationToken)
    {
        var response = await record.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Requires the separately named privacy owner to adopt the exact current source and generation.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("adoptions", Name = RouteNames.AdoptModerationCaseNarrativePolicy)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Adopt(
        [FromBody] AdoptModerationCaseNarrativePolicyCommand command, CancellationToken cancellationToken)
    {
        var response = await adopt.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Withdraws custody authority without rewriting original capture deadlines or acknowledgement history.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("adoptions/{adoptionId:guid}/withdraw", Name = RouteNames.WithdrawModerationCaseNarrativePolicy)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Withdraw(Guid adoptionId,
        [FromBody] WithdrawModerationCaseNarrativePolicyCommand command, CancellationToken cancellationToken)
    {
        var response = await withdraw.ExecuteAsync(command with { AdoptionId = adoptionId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
