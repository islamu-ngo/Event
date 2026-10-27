using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Responses;
using Explore.Application.Features.Moderation.Responses.Commands;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/moderation/pre-finding/cases/{caseId:guid}/custodies")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(16 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationPreFindingCustodyController(
    ICommandHandler<ProposeModerationPreFindingCustodyCommand, BaseCommandResponse<Guid>> propose,
    ICommandHandler<AcceptModerationPreFindingCustodyCommand, BaseCommandResponse<Guid>> accept,
    ICommandHandler<WithdrawModerationPreFindingCustodyCommand, BaseCommandResponse<Guid>> withdraw) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid custody operation", "The pre-finding custody intent is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .NotFound(new("Custody unavailable", "The selected pre-finding custody is unavailable."), FailureCodes.NotFound)
        .Conflict("Custody conflict", "The original custody or operation receipt cannot be replaced.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Custody unavailable", "Current pre-finding custody authority is unavailable.", "ModerationPreFindingUnavailable");

    /// <summary>Proposes explicit finite privacy terms without accepting custody or returning private case material.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost(Name = RouteNames.ProposeModerationPreFindingCustody)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Propose(Guid caseId,
        [FromBody] ProposeModerationPreFindingCustodyCommand command, CancellationToken cancellationToken)
    {
        var response = await propose.ExecuteAsync(command with { CaseId = caseId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Independently accepts the selected original proposal under current native privacy and reviewer authority.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("{custodyId:guid}/acceptance", Name = RouteNames.AcceptModerationPreFindingCustody)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Accept(Guid caseId, Guid custodyId,
        [FromBody] AcceptModerationPreFindingCustodyCommand command, CancellationToken cancellationToken)
    {
        var response = await accept.ExecuteAsync(command with { CaseId = caseId, CustodyId = custodyId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Withdraws exact finite custody through its native privacy owner without extending the original calendar.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("{custodyId:guid}/withdrawal", Name = RouteNames.WithdrawModerationPreFindingCustody)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Withdraw(Guid caseId, Guid custodyId,
        [FromBody] WithdrawModerationPreFindingCustodyCommand command, CancellationToken cancellationToken)
    {
        var response = await withdraw.ExecuteAsync(command with { CaseId = caseId, CustodyId = custodyId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
