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
[Route("api/moderation/policies/{policyId:guid}/publications")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(4 * 1024 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationPolicyPublicationController(
    ICommandHandler<PublishModerationPolicyCommand, ModerationPolicyPublicationResponse> publish) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid publication intent", "The moderation publication intent is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Publication forbidden", "Current native policy publication authority is required.", FailureCodes.AdminRequired)
        .Conflict("Publication conflict", "The expected native publication coordinates are no longer current.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Publication unavailable", "Current native policy publication authority is unavailable.", "ModerationPublicationUnavailable");

    /// <summary>Publishes complete policy intent under native authority; PublishedActive describes only the publication instant.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost(Name = RouteNames.PublishModerationPolicy)]
    [ProducesResponseType<ModerationPolicyPublicationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationPolicyPublicationResponse>> Publish(Guid policyId,
        [FromBody] PublishModerationPolicyCommand command, CancellationToken cancellationToken)
    {
        if (command.Request is { Proposed: { } proposed } request)
            command = command with { Request = request with { Proposed = proposed with { PolicyId = policyId } } };
        var response = await publish.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
