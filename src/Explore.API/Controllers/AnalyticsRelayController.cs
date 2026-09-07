using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Analytics;
using Explore.Application.Features.PublicExperience.Requests.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/a/t")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public class AnalyticsRelayController(IMediator mediator) : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor RelayValidationProblem = new(
        "analyticsRelay",
        "Analytics relay validation failed",
        "Analytics relay rejected the submitted event.");

    [HttpPost(Name = RouteNames.RelayAnalyticsEvent)]
    [AllowAnonymous]
    [EnableRateLimiting(Extensions.RateLimitingExtensions.AnalyticsRelayPolicy)]
    [EndpointSummary("Relay Browser Analytics Event")]
    [EndpointDescription("Relays browser analytics through the server for tenants using relay transport mode.")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Relay([FromBody] RelayAnalyticsEventDto payload, CancellationToken cancellationToken)
    {
        var accepted = await mediator.Send(new RelayAnalyticsEventCommand
        {
            AuthenticatedUserId = CurrentUserId,
            Payload = payload
        }, cancellationToken);

        return accepted
            ? Accepted()
            : this.ToValidationProblem(
                RelayValidationProblem,
                "Analytics relay rejected the submitted event.",
                ApiProblemCodes.AnalyticsRelayRejected);
    }
}
