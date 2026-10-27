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
[Route("api/moderation/profile-subject-consents")]
[Authorize]
[Consumes("application/json")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("write")]
[RequestSizeLimit(64 * 1024)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
public sealed class ModerationProfileSubjectConsentsController(
    ICommandHandler<GrantModerationProfileSubjectConsentCommand, BaseCommandResponse<Guid>> grant,
    ICommandHandler<WithdrawModerationProfileSubjectConsentCommand, BaseCommandResponse<Guid>> withdraw) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid profile consent request", "The profile consent request is invalid."))
        .NotFound(new("Profile consent unavailable", "The requested profile consent is unavailable."), FailureCodes.NotFound);

    /// <summary>Records the actual original subject's demanded self-consent, never guardian permission or a collection receipt.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost(Name = RouteNames.GrantModerationProfileSubjectConsent)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Grant(
        [FromBody] GrantModerationProfileSubjectConsentCommand command, CancellationToken cancellationToken)
    {
        var response = await grant.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }

    /// <summary>Terminal route-bound original-self withdrawal without requiring a live policy or assessment.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost("{consentId:guid}/withdraw", Name = RouteNames.WithdrawModerationProfileSubjectConsent)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Withdraw(Guid consentId,
        [FromBody] WithdrawModerationProfileSubjectConsentCommand command, CancellationToken cancellationToken)
    {
        var response = await withdraw.ExecuteAsync(command with { ConsentId = consentId }, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
