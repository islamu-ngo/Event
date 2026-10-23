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
[Route("api/moderation/decisions/{decisionId:guid}/sanctions/{supersededSanctionId:guid}/supersession")]
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
public sealed class ModerationSanctionSupersessionController(
    ICommandHandler<SupersedeModerationSanctionCommand, ModerationLifecycleResponse> supersede) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid supersession intent", "The moderation supersession intent is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Supersession forbidden", "Current native authority over both correction scopes is required.", FailureCodes.AdminRequired)
        .NotFound(new("Contribution unavailable", "The selected replacement decision or original contribution is unavailable."), FailureCodes.NotFound)
        .Conflict("Supersession conflict", "The expected decision, original contribution or policy revision is no longer current.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Supersession unavailable", "Complete current moderation correction qualification is unavailable.", "ModerationAuthorityUnavailable");

    /// <summary>Supersedes the named original only with the existing approved replacement decision and qualified old/new scopes.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost(Name = RouteNames.SupersedeModerationSanction)]
    [ProducesResponseType<ModerationLifecycleResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationLifecycleResponse>> Supersede(Guid decisionId, Guid supersededSanctionId,
        [FromBody] SupersedeModerationSanctionCommand command, CancellationToken cancellationToken)
    {
        var response = await supersede.ExecuteAsync(
            command with { DecisionId = decisionId, SupersededSanctionId = supersededSanctionId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
