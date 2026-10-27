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
[Route("api/moderation/profile-collection-subject-observations")]
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
public sealed class ModerationProfileCollectionSubjectObservationsController(
    ICommandHandler<ObserveModerationProfileCollectionSubjectCommand, BaseCommandResponse<Guid>> observe) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid profile observation", "The profile observation nomination is invalid."))
        .NotFound(new("Profile observation unavailable", "Current native subject observation is unavailable."), FailureCodes.NotFound);

    /// <summary>Observes only the actual authenticated existing Human against the nominated native profile and mode.</summary>
    [SuppressIdempotencyResponseStorage]
    [HttpPost(Name = RouteNames.ObserveModerationProfileCollectionSubject)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Observe(
        [FromBody] ObserveModerationProfileCollectionSubjectCommand command, CancellationToken cancellationToken)
    {
        var response = await observe.ExecuteAsync(command, cancellationToken);
        return Failures.Map(this, response, () => Ok(response));
    }
}
