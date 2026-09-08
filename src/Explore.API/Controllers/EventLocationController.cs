using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.EventLocations.Requests.Commands;
using Explore.Application.Features.EventLocations.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/locations")]
[ApiController]
public sealed class EventLocationController(
    IMediator mediator,
    IResourceAssembler<EventLocationManagementDto, EventLocationManagementDto> resourceAssembler)
    : ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor EventLocationNotFoundProblem = new(
        "Event location not found",
        "The requested event location was not found.");

    private static readonly ApiValidationProblemDescriptor DisclosureValidationProblem = new(
        "eventLocationDisclosure",
        "Event location disclosure validation failed",
        "The EventLocation disclosure policy could not be updated.");

    private static readonly ApiValidationProblemDescriptor RemediationValidationProblem = new(
        "eventLocationRemediation",
        "Event location remediation failed",
        "The EventLocation privacy review could not be completed.");

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("", Name = RouteNames.GetPublicEventLocations)]
    [EndpointSummary("Get public event locations")]
    [EndpointDescription("Returns only public-purpose EventLocation disclosures for a published public event.")]
    [ProducesResponseType(typeof(IReadOnlyList<EventLocationPublicDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<EventLocationPublicDto>>> GetPublic(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EventLocationPublicDto>? result = await mediator.Send(
            new GetPublicEventLocationsRequest(eventId),
            cancellationToken);
        return result is null
            ? this.ToNotFoundProblem(EventLocationNotFoundProblem)
            : Ok(result);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("my-access", Name = RouteNames.GetAttendeeEventLocations)]
    [EndpointSummary("Get attendee event location access")]
    [EndpointDescription("Returns registration-scoped EventLocation disclosures for the authenticated requester.")]
    [ProducesResponseType(typeof(IReadOnlyList<EventLocationAttendeeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<EventLocationAttendeeDto>>> GetMyAccess(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EventLocationAttendeeDto>? result = await mediator.Send(
            new GetAttendeeEventLocationsRequest(eventId),
            cancellationToken);
        return result is null
            ? this.ToNotFoundProblem(EventLocationNotFoundProblem)
            : Ok(result);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("{eventLocationId:guid}/management", Name = RouteNames.GetManagementEventLocation)]
    [EndpointSummary("Get managed event location")]
    [EndpointDescription("Returns exact operational and disclosure-policy details after event management authorization.")]
    [ProducesResponseType(typeof(HalResource<EventLocationManagementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventLocationManagementDto>>> GetManagement(
        Guid eventId,
        Guid eventLocationId,
        CancellationToken cancellationToken = default)
    {
        EventLocationManagementDto? result = await mediator.Send(
            new GetManagementEventLocationRequest(eventId, eventLocationId),
            cancellationToken);
        return result is null
            ? this.ToNotFoundProblem(EventLocationNotFoundProblem)
            : Ok(await resourceAssembler.ToResource(result, HttpContext));
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("management", Name = RouteNames.GetManagementEventLocations)]
    [EndpointSummary("Get managed event locations")]
    [EndpointDescription("Returns every EventLocation attached to the event with its disclosure policy and affordances.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventLocationManagementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalCollectionResource<EventLocationManagementDto>>> GetManagementList(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EventLocationManagementDto>? result = await mediator.Send(
            new GetManagementEventLocationsRequest(eventId),
            cancellationToken);
        if (result is null)
        {
            return this.ToNotFoundProblem(EventLocationNotFoundProblem);
        }

        HalCollectionResource<EventLocationManagementDto> resource =
            await resourceAssembler.ToCollectionResource(
                result,
                RouteNames.GetManagementEventLocations,
                new { eventId },
                HttpContext);
        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("review", Name = RouteNames.GetEventLocationReviewQueue)]
    [EndpointSummary("Get event location privacy review queue")]
    [EndpointDescription("Returns management-authorized EventLocations that still require privacy remediation.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventLocationManagementDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalCollectionResource<EventLocationManagementDto>>> GetReviewQueue(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EventLocationManagementDto>? result = await mediator.Send(
            new GetEventLocationReviewQueueRequest(eventId),
            cancellationToken);
        if (result is null)
        {
            return this.ToNotFoundProblem(EventLocationNotFoundProblem);
        }

        HalCollectionResource<EventLocationManagementDto> resource =
            await resourceAssembler.ToCollectionResource(
                result,
                RouteNames.GetEventLocationReviewQueue,
                new { eventId },
                HttpContext);
        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpPatch("{eventLocationId:guid}/disclosure", Name = RouteNames.UpdateEventLocationDisclosure)]
    [EndpointSummary("Update event location disclosure")]
    [EndpointDescription("Updates organizer-selected disclosure fields using required policy and aggregate concurrency tokens.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateDisclosure(
        Guid eventId,
        Guid eventLocationId,
        [FromBody] UpdateEventLocationDisclosureDto request,
        CancellationToken cancellationToken = default)
    {
        BaseCommandResponse<Guid> response = await mediator.Send(
            new UpdateEventLocationPolicyCommand
            {
                EventId = eventId,
                EventLocationId = eventLocationId,
                ExpectedConcurrencyStamp = request.ExpectedConcurrencyStamp,
                ExpectedPolicyVersion = request.ExpectedPolicyVersion,
                Fields = request.Fields,
                Audience = request.Audience,
                NeedsPrivacyReview = false
            },
            cancellationToken);

        if (response.IsSuccess)
        {
            return Ok(response);
        }

        return response.FailureCode == "event_location_policy_not_found"
            ? this.ToNotFoundProblem(EventLocationNotFoundProblem, response.Message)
            : this.ToCommandValidationProblem(response, DisclosureValidationProblem);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpPost("{eventLocationId:guid}/remediation/confirm", Name = RouteNames.ConfirmEventLocationRemediation)]
    [EndpointSummary("Confirm event location privacy remediation")]
    [EndpointDescription("Clears privacy review only for a usable physical EventLocation or an explicit TBA association.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ConfirmRemediation(
        Guid eventId,
        Guid eventLocationId,
        [FromBody] ConfirmEventLocationRemediationDto request,
        CancellationToken cancellationToken = default)
    {
        BaseCommandResponse<Guid> response = await mediator.Send(
            new ConfirmEventLocationRemediationCommand
            {
                EventId = eventId,
                EventLocationId = eventLocationId,
                ExpectedConcurrencyStamp = request.ExpectedConcurrencyStamp,
                ExpectedPolicyVersion = request.ExpectedPolicyVersion
            },
            cancellationToken);

        if (response.IsSuccess)
        {
            return Ok(response);
        }

        return response.FailureCode == "event_location_remediation_not_found"
            ? this.ToNotFoundProblem(EventLocationNotFoundProblem, response.Message)
            : this.ToCommandValidationProblem(response, RemediationValidationProblem);
    }

}
