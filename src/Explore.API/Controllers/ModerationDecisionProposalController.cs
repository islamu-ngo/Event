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
[Route("api/moderation/decisions/{decisionId:guid}/proposal")]
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
public sealed class ModerationDecisionProposalController(
    ICommandHandler<ProposeModerationDecisionCommand, ModerationLifecycleResponse> propose) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid proposal intent", "The moderation proposal intent is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Proposal forbidden", "Current native proposal authority is required.", FailureCodes.AdminRequired)
        .NotFound(new("Case unavailable", "The selected open moderation case is unavailable."), FailureCodes.NotFound)
        .Conflict("Proposal conflict", "The expected case or policy revision is no longer current.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Proposal unavailable", "Current moderation proposal authority is unavailable.", "ModerationAuthorityUnavailable");

    /// <summary>Proposes explicit case/rule/capability/quorum/interval terms without accepting a finding or activating a restriction.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost(Name = RouteNames.ProposeModerationDecision)]
    [ProducesResponseType<ModerationLifecycleResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationLifecycleResponse>> Propose(Guid decisionId,
        [FromBody] ProposeModerationDecisionCommand command, CancellationToken cancellationToken)
    {
        var response = await propose.ExecuteAsync(command with { DecisionId = decisionId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
