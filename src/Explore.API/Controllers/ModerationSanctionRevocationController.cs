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
[Route("api/moderation/decisions/{decisionId:guid}/sanctions/{sanctionId:guid}/revocation")]
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
public sealed class ModerationSanctionRevocationController(
    ICommandHandler<RevokeModerationSanctionCommand, ModerationLifecycleResponse> revoke) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid revocation intent", "The moderation revocation intent is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Revocation forbidden", "Current native correction authority is required.", FailureCodes.AdminRequired)
        .NotFound(new("Contribution unavailable", "The selected moderation decision or contribution is unavailable."), FailureCodes.NotFound)
        .Conflict("Revocation conflict", "The expected decision or policy revision is no longer current.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Revocation unavailable", "Current moderation correction authority is unavailable.", "ModerationAuthorityUnavailable");

    /// <summary>Revokes only the selected contribution through its native historical owner and current corrector.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost(Name = RouteNames.RevokeModerationSanction)]
    [ProducesResponseType<ModerationLifecycleResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationLifecycleResponse>> Revoke(Guid decisionId, Guid sanctionId,
        [FromBody] RevokeModerationSanctionCommand command, CancellationToken cancellationToken)
    {
        var response = await revoke.ExecuteAsync(
            command with { DecisionId = decisionId, SanctionId = sanctionId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
