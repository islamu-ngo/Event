using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/registration-orders")]
[ApiController]
[Tags("GuestRegistrationOrder")]
public sealed class GuestRegistrationOrderClaimController(
    IMediator mediator) : RegistrationOrderControllerBase
{
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [HttpPost("guest/{orderId:guid}/claim", Name = RouteNames.ClaimGuestRegistrationOrder)]
    [EndpointSummary("Claim guest registration order")]
    [EndpointDescription("Links a guest registration order to the authenticated current account only when the guest capability and verified account email match.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ClaimGuest(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        CancellationToken cancellationToken = default)
    {
        BaseCommandResponse<Guid> response = await mediator.Send(
            new ClaimGuestRegistrationOrderCommand(eventId, orderId, capability), cancellationToken);
        return response.IsSuccess
            ? Ok(response)
            : response.FailureCode switch
            {
                "registration_order_authentication_required" => this.ToAuthenticationRequiredProblem(),
                "registration_order_not_found" => this.ToNotFoundProblem(RegistrationOrderNotFoundProblem),
                "registration_order_already_linked" => Conflict(response),
                _ => this.ToCommandValidationProblem(response, RegistrationOrderValidationProblem)
            };
    }
}
