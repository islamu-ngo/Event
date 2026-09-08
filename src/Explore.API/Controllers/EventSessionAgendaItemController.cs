using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;
using Explore.Application.Features.EventSessionAgendaItems.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
public class EventSessionAgendaItemController : ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor AgendaItemNotFoundProblem = new(
        "Event session agenda item not found",
        "Event session agenda item not found.");

    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "eventSessionAgendaItem",
        "Event session agenda item validation failed",
        "Event session agenda item creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "eventSessionAgendaItem",
        "Event session agenda item validation failed",
        "Event session agenda item update failed.");

    private readonly IMediator _mediator;
    private readonly ILogger<EventSessionAgendaItemController> _logger;

    public EventSessionAgendaItemController(IMediator mediator, ILogger<EventSessionAgendaItemController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    // GET: api/eventsessionagendaitem
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet(Name = RouteNames.GetEventSessionAgendaItems)]
    [EndpointSummary("Get all Agenda Items")]
    [EndpointDescription("Retrieve a paginated list of all event session agenda items. Default page size is 20, max is 100.")]
    [ProducesResponseType(typeof(PaginatedResult<EventSessionAgendaItemListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginatedResult<EventSessionAgendaItemListDto>>> GetAll(
        [FromQuery] PaginationQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var agendaItems = await _mediator.Send(new GetEventSessionAgendaItemListRequest
        {
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);
        return Ok(agendaItems);
    }

    // GET: api/eventsessionagendaitem/{id}
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id}", Name = RouteNames.GetEventSessionAgendaItemById)]
    [EndpointSummary("Get Agenda Item Details")]
    [EndpointDescription("Get detailed information about a specific agenda item")]
    [ProducesResponseType(typeof(EventSessionAgendaItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventSessionAgendaItemDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var agendaItem = await _mediator.Send(new GetEventSessionAgendaItemDetailsRequest { Id = id }, cancellationToken);
        if (agendaItem is null)
        {
            return this.ToNotFoundProblem(AgendaItemNotFoundProblem);
        }

        return Ok(agendaItem);
    }

    // GET: api/eventsessionagendaitem/by-session/{sessionId}
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("by-session/{sessionId}", Name = RouteNames.GetEventSessionAgendaItemsBySession)]
    [EndpointSummary("Get Agenda Items by Session")]
    [EndpointDescription("Get all agenda items for a specific event session")]
    public async Task<ActionResult<List<EventSessionAgendaItemListDto>>> GetBySession(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var agendaItems = await _mediator.Send(new GetAgendaItemsBySessionRequest { EventSessionId = sessionId }, cancellationToken);
        return Ok(agendaItems);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet(
        "management/by-event/{eventId:guid}/by-session/{sessionId:guid}",
        Name = RouteNames.GetManagedEventSessionAgendaItemsBySession)]
    [EndpointSummary("Get Managed Agenda Items by Session")]
    [EndpointDescription("Returns exact session agenda items for an authorized event management surface.")]
    [ProducesResponseType(typeof(List<EventSessionAgendaItemListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<EventSessionAgendaItemListDto>>> GetManagedBySession(
        Guid eventId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var agendaItems = await _mediator.Send(new GetManagedAgendaItemsBySessionRequest
        {
            EventId = eventId,
            EventSessionId = sessionId
        }, cancellationToken);
        if (agendaItems is null)
        {
            return this.ToNotFoundProblem(AgendaItemNotFoundProblem);
        }

        return Ok(agendaItems);
    }

    // POST: api/eventsessionagendaitem
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateEventSessionAgendaItem)]
    [EndpointSummary("Create Agenda Item")]
    [EndpointDescription("Create a new agenda item for an event session")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateEventSessionAgendaItemDto agendaItem, CancellationToken cancellationToken = default)
    {
        var command = new CreateEventSessionAgendaItemCommand { AgendaItemDto = agendaItem };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return Ok(response);
    }

    // PATCH: api/eventsessionagendaitem/{id}
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateEventSessionAgendaItem)]
    [EndpointSummary("Update Agenda Item")]
    [EndpointDescription("Partially update an existing agenda item. Route identity is authoritative.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id,
        [FromBody] UpdateEventSessionAgendaItemDto agendaItem,
        CancellationToken cancellationToken = default)
    {
        var command = new UpdateEventSessionAgendaItemCommand
        {
            EventSessionAgendaItemId = id,
            AgendaItemDto = agendaItem
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    // DELETE: api/eventsessionagendaitem/{id}
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id}", Name = RouteNames.DeleteEventSessionAgendaItem)]
    [EndpointSummary("Delete Agenda Item")]
    [EndpointDescription("Delete an agenda item")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var command = new DeleteEventSessionAgendaItemCommand { Id = id };
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }
}
