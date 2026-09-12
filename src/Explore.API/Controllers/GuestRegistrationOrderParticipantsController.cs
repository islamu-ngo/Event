using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Hateoas;
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
public sealed class GuestRegistrationOrderParticipantsController(
    IMediator mediator) : RegistrationOrderControllerBase
{
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [HttpGet("guest/{orderId:guid}/participants", Name = RouteNames.GetGuestRegistrationOrderParticipants)]
    [EndpointSummary("Get guest registration participants")]
    [ProducesResponseType(typeof(HalResource<RegistrationOrderParticipantsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationOrderParticipantsDto>>> GetGuestParticipants(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        CancellationToken cancellationToken = default)
    {
        RegistrationOrderParticipantsDto? response = await mediator.Send(
            new GetGuestRegistrationOrderParticipantsQuery(eventId, orderId, capability), cancellationToken);
        return response is null
            ? this.ToNotFoundProblem(RegistrationOrderNotFoundProblem)
            : Ok(ToParticipantsHalResource(response, eventId, guest: true));
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPost("guest/{orderId:guid}/participants", Name = RouteNames.AddGuestRegistrationOrderParticipant)]
    [EndpointSummary("Add guest registration participant")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> AddGuestParticipant(
        Guid eventId, Guid orderId, [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] RegistrationParticipantRequest request, CancellationToken cancellationToken = default) =>
        MutateGuest(eventId, orderId, capability,
            new AddRegistrationParticipantCommand(orderId, request.ParticipantTypeId, request.GuardianParticipantId, request.Details),
            cancellationToken);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPut("guest/{orderId:guid}/participants/{participantId:guid}", Name = RouteNames.UpdateGuestRegistrationOrderParticipant)]
    [EndpointSummary("Update guest registration participant")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> UpdateGuestParticipant(
        Guid eventId, Guid orderId, Guid participantId, [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] RegistrationParticipantRequest request, CancellationToken cancellationToken = default) =>
        MutateGuest(eventId, orderId, capability,
            new UpdateRegistrationParticipantCommand(orderId, participantId, request.ParticipantTypeId, request.GuardianParticipantId, request.Details),
            cancellationToken);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPut("guest/{orderId:guid}/assignments", Name = RouteNames.AssignGuestRegistrationOrderTickets)]
    [EndpointSummary("Assign guest registration tickets")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> AssignGuestTickets(
        Guid eventId, Guid orderId, [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] RegistrationTicketAssignmentsRequest request, CancellationToken cancellationToken = default) =>
        MutateGuest(eventId, orderId, capability,
            new BulkAssignRegistrationTicketsCommand(orderId, request.Assignments), cancellationToken);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPut("guest/{orderId:guid}/assignments/deferred", Name = RouteNames.DeferGuestRegistrationOrderTickets)]
    [EndpointSummary("Defer guest registration ticket assignments")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> DeferGuestTickets(
        Guid eventId, Guid orderId, [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] RegistrationTicketDeferralsRequest request, CancellationToken cancellationToken = default) =>
        MutateGuest(eventId, orderId, capability,
            new BulkDeferRegistrationTicketsCommand(orderId, request.Assignments, request.AssignmentDeadline), cancellationToken);

    private async Task<ActionResult<BaseCommandResponse<Guid>>> MutateGuest(
        Guid eventId, Guid orderId, string? capability, IRegistrationParticipantMutation mutation,
        CancellationToken cancellationToken) => MapParticipantMutation(await mediator.Send(
            new MutateGuestRegistrationParticipantsCommand(eventId, orderId, capability, mutation), cancellationToken));
}
