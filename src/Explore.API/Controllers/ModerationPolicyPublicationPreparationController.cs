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
[Route("api/moderation/policies/{policyId:guid}/publication-preparations")]
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
public sealed class ModerationPolicyPublicationPreparationController(
    ICommandHandler<PrepareModerationPolicyPublicationCommand, ModerationPolicyPublicationCandidateResponse> prepare) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid preparation intent", "The moderation publication preparation intent is invalid."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Preparation forbidden", "Current native policy preparation authority is required.", FailureCodes.AdminRequired)
        .Conflict("Preparation conflict", "The expected native publication coordinates are no longer current.", FailureCodes.ConcurrencyConflict)
        .Unavailable("Preparation unavailable", "Current native policy preparation authority is unavailable.", "ModerationPublicationPreparationUnavailable");

    /// <summary>Prepares an exact future active intent for measurement without publishing or activating policy.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost(Name = RouteNames.PrepareModerationPolicyPublication)]
    [ProducesResponseType<ModerationPolicyPublicationCandidateResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ModerationPolicyPublicationCandidateResponse>> Prepare(Guid policyId,
        [FromBody] PrepareModerationPolicyPublicationCommand command, CancellationToken cancellationToken)
    {
        if (command.Request is { Proposed: { } proposed } request)
            command = command with { Request = request with { Proposed = proposed with { PolicyId = policyId } } };
        var response = await prepare.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
