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
[Route("api/moderation/operational-budgets")]
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
public sealed class ModerationOperationalBudgetsController(
    ICommandHandler<RecordModerationOperationalBudgetCommand, ModerationQualificationResponse> record,
    ICommandHandler<AdoptModerationOperationalBudgetCommand, ModerationQualificationResponse> adopt,
    ICommandHandler<WithdrawModerationOperationalBudgetCommand, ModerationQualificationResponse> withdraw) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid operational budget", "The operational budget request is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Operational budget forbidden", "Current native human authority is required.", FailureCodes.AdminRequired)
        .NotFound(new("Operational budget unavailable", "The requested operational budget is unavailable."), FailureCodes.NotFound)
        .Conflict("Operational budget conflict", "The original intent or generation does not match.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Operational budget unavailable", "Current native qualification is unavailable.", "ModerationAuthorityUnavailable");

    /// <summary>Records finite Human capacity terms for independent operator acceptance, without granting an action.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("sources", Name = RouteNames.RecordModerationOperationalBudget)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Record(
        [FromBody] RecordModerationOperationalBudgetCommand command, CancellationToken cancellationToken)
    {
        var response = await record.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Requires the named independent native operator to accept the exact selected source generation.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("adoptions", Name = RouteNames.AdoptModerationOperationalBudget)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Adopt(
        [FromBody] AdoptModerationOperationalBudgetCommand command, CancellationToken cancellationToken)
    {
        var response = await adopt.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Withdraws budget selection without resetting original successful-operation history.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("adoptions/{adoptionId:guid}/withdraw", Name = RouteNames.WithdrawModerationOperationalBudget)]
    [ProducesResponseType<ModerationQualificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationQualificationResponse>> Withdraw(Guid adoptionId,
        [FromBody] WithdrawModerationOperationalBudgetCommand command, CancellationToken cancellationToken)
    {
        var response = await withdraw.ExecuteAsync(command with { AdoptionId = adoptionId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
