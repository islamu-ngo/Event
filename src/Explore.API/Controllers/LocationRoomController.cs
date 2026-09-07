using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.LocationRoom;
using Explore.Application.Features.LocationRooms.Requests.Commands;
using Explore.Application.Features.LocationRooms.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

/// <summary>
/// Location Room management API endpoints.
/// Rooms represent bookable spaces within a location for session scheduling.
/// All responses include HATEOAS links by default.
/// Send "Prefer: return=minimal" header to strip links.
/// </summary>
[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class LocationRoomController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "locationRoom",
        "Location room validation failed",
        "Location room creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "locationRoom",
        "Location room validation failed",
        "Location room update failed.");

    private static readonly ApiNotFoundProblemDescriptor LocationRoomNotFoundProblem = new(
        "Location room not found",
        "Location room not found.");

    private readonly IMediator _mediator;
    private readonly ILogger<LocationRoomController> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IResourceAssembler<LocationRoomDto, LocationRoomListDto> _resourceAssembler;

    public LocationRoomController(
        IMediator mediator,
        ILogger<LocationRoomController> logger,
        ITenantContext tenantContext,
        IResourceAssembler<LocationRoomDto, LocationRoomListDto> resourceAssembler)
    {
        _mediator = mediator;
        _logger = logger;
        _tenantContext = tenantContext;
        _resourceAssembler = resourceAssembler;
    }

    /// <summary>
    /// Get all rooms for a specific location.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("by-location/{locationId:guid}", Name = RouteNames.GetLocationRoomsByLocation)]
    [EndpointSummary("Get Rooms by Location")]
    [EndpointDescription("Get all rooms for a specific location, ordered by sort order.")]
    [ProducesResponseType(typeof(HalCollectionResource<LocationRoomListDto>), StatusCodes.Status200OK)]
    [PrivateNoStore]
    public async Task<ActionResult<HalCollectionResource<LocationRoomListDto>>> GetByLocation(Guid locationId, CancellationToken cancellationToken = default)
    {
        var rooms = await _mediator.Send(new GetLocationRoomsByLocationRequest
        {
            LocationId = locationId,
            TenantId = _tenantContext.TenantId
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            rooms,
            RouteNames.GetLocationRoomsByLocation,
            new { locationId },
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get location room details by ID.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("{id:guid}", Name = RouteNames.GetLocationRoomById)]
    [EndpointSummary("Get Room Details")]
    [EndpointDescription("Get detailed information about a specific location room.")]
    [ProducesResponseType(typeof(HalResource<LocationRoomDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [PrivateNoStore]
    public async Task<ActionResult<HalResource<LocationRoomDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var room = await _mediator.Send(new GetLocationRoomDetailRequest
        {
            Id = id,
            TenantId = _tenantContext.TenantId
        }, cancellationToken);
        if (room == null)
        {
            return this.ToNotFoundProblem(LocationRoomNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(room, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Create a new location room.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateLocationRoom)]
    [EndpointSummary("Create Room")]
    [EndpointDescription("Create a new room within a location.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateLocationRoomDto room, CancellationToken cancellationToken = default)
    {
        var command = new CreateLocationRoomCommand { LocationRoomDto = room };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetLocationRoomById,
            new { id = response.Id },
            response);
    }

    /// <summary>
    /// Partially update an existing location room.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateLocationRoom)]
    [EndpointSummary("Update Room")]
    [EndpointDescription("Partially update an existing location room. Route ID is authoritative and If-Match must contain the current location room concurrency stamp.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id,
        [FromBody] UpdateLocationRoomDto room,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                UpdateValidationProblem,
                "If-Match header is required and must contain the current location room concurrency stamp.");
        }

        var command = new UpdateLocationRoomCommand
        {
            LocationRoomId = id,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp,
            UpdateLocationRoomDto = room
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(LocationRoomNotFoundProblem)
                : this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Delete a location room.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteLocationRoom)]
    [EndpointSummary("Delete Room")]
    [EndpointDescription("Delete a location room.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var command = new DeleteLocationRoomCommand { Id = id };
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }
}
