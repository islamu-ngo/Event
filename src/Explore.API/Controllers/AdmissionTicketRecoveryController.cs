using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.DTOs.AdmissionTickets;
using Explore.Application.Features.AdmissionTickets.Requests.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/tickets")]
public sealed class AdmissionTicketRecoveryController(IMediator mediator) : ControllerBase
{
    private const string RecoveryCapabilityHeader =
        "X-Admission-Ticket-Recovery-Capability";
    private static readonly ApiNotFoundProblemDescriptor RecoveryNotFound = new(
        "Admission ticket not found",
        "The requested admission ticket was not found.");

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPost("recovery", Name = RouteNames.RequestAdmissionTicketRecovery)]
    [EndpointSummary("Request admission ticket recovery")]
    [ProducesResponseType(typeof(AdmissionTicketRecoveryRequestResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public new async Task<ActionResult<AdmissionTicketRecoveryRequestResultDto>> Request(
        [FromBody] RequestAdmissionTicketRecoveryCommand? request,
        CancellationToken cancellationToken)
    {
        AdmissionTicketRecoveryRequestResultDto result = await mediator.Send(
            request ?? new RequestAdmissionTicketRecoveryCommand(string.Empty),
            cancellationToken);
        return Accepted(result);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EnableRateLimiting(RateLimitingExtensions.AdmissionTicketRecoveryPolicy)]
    [PrivateNoStore]
    [HttpPost("recovery/consume", Name = RouteNames.ConsumeAdmissionTicketRecovery)]
    [EndpointSummary("Consume admission ticket recovery capability")]
    [ProducesResponseType(typeof(AdmissionTicketRecoveryDeliveryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AdmissionTicketRecoveryDeliveryDto>> Consume(
        [FromHeader(Name = RecoveryCapabilityHeader)] string? capability,
        CancellationToken cancellationToken)
    {
        AdmissionTicketRecoveryConsumeResultDto? result = await mediator.Send(
            new RedeemAdmissionTicketRecoveryCommand(capability ?? string.Empty),
            cancellationToken);
        return result is null
            ? this.ToNotFoundProblem(RecoveryNotFound)
            : Ok(result.Delivery);
    }
}
