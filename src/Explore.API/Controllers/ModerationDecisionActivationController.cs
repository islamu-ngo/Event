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
[Route("api/moderation/decisions/{decisionId:guid}/activation")]
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
public sealed class ModerationDecisionActivationController(
    ICommandHandler<ActivateModerationDecisionCommand, ModerationLifecycleResponse> activate) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid activation intent", "The moderation activation intent is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Activation forbidden", "Current native activation authority is required.", FailureCodes.AdminRequired)
        .NotFound(new("Decision unavailable", "The selected moderation decision is unavailable."), FailureCodes.NotFound)
        .Conflict("Activation conflict", "The expected decision or policy revision is no longer current.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Activation unavailable", "Complete current moderation qualification is unavailable.", "ModerationAuthorityUnavailable");

    /// <summary>Activates or schedules the existing interval only after complete current native qualification.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost(Name = RouteNames.ActivateModerationDecision)]
    [ProducesResponseType<ModerationLifecycleResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationLifecycleResponse>> Activate(Guid decisionId,
        [FromBody] ActivateModerationDecisionCommand command, CancellationToken cancellationToken)
    {
        var response = await activate.ExecuteAsync(command with { DecisionId = decisionId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
