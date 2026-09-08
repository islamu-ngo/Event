using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Features.EventSeries.Requests.Commands;
using Explore.Application.Features.EventSeries.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class EventSeriesController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "eventSeries",
        "Event series validation failed",
        "Event series creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "eventSeries",
        "Event series validation failed",
        "Event series update failed.");

    private static readonly ApiNotFoundProblemDescriptor NotFoundProblem = new(
        "Event series not found",
        "The requested event series could not be found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<EventSeriesDto, EventSeriesListDto> _resourceAssembler;

    public EventSeriesController(
        IMediator mediator,
        IResourceAssembler<EventSeriesDto, EventSeriesListDto> resourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet(Name = RouteNames.GetEventSeries)]
    [ProducesResponseType(typeof(HalCollectionResource<EventSeriesListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HalCollectionResource<EventSeriesListDto>>> GetAll(
        [FromQuery] EventSeriesListQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetEventSeriesListRequest
        {
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            ActorId = query.ActorId
        }, cancellationToken);
        var resource = await _resourceAssembler.ToCollectionResource(
            response,
            RouteNames.GetEventSeries,
            new { actorId = query.ActorId },
            HttpContext);
        return Ok(resource);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}", Name = RouteNames.GetEventSeriesById)]
    [ProducesResponseType(typeof(HalResource<EventSeriesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<EventSeriesDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetEventSeriesDetailRequest { Id = id }, cancellationToken);
        if (response is null)
        {
            return this.ToNotFoundProblem(NotFoundProblem);
        }

        return Ok(await _resourceAssembler.ToResource(response, HttpContext));
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("top", Name = RouteNames.GetTopEventSeries)]
    [ProducesResponseType(typeof(HalResource<EventSeriesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<HalResource<EventSeriesDto>>> GetTop(
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetTopEventSeriesRequest(), cancellationToken);
        if (response == null)
        {
            return NoContent();
        }
        return Ok(await _resourceAssembler.ToResource(response, HttpContext));
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateEventSeries)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateEventSeriesDto dto)
    {
        var response = await _mediator.Send(new CreateEventSeriesCommand { EventSeriesDto = dto });
        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateEventSeries)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id,
        [FromBody] UpdateEventSeriesDto dto,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                UpdateValidationProblem,
                "If-Match header is required and must contain the current event series concurrency stamp.");
        }

        var existing = await _mediator.Send(new GetEventSeriesDetailRequest { Id = id }, cancellationToken);
        if (existing is null)
        {
            return this.ToNotFoundProblem(NotFoundProblem);
        }

        var response = await _mediator.Send(new UpdateEventSeriesCommand
        {
            EventSeriesId = id,
            ActorId = existing.ActorId,
            TenantId = existing.TenantId,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp,
            EventSeriesDto = dto
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(NotFoundProblem)
                : this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }
        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteEventSeries)]
    [ProducesResponseType(typeof(BaseCommandResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<bool>>> Delete(Guid id)
    {
        var response = await _mediator.Send(new DeleteEventSeriesCommand { Id = id });
        if (!response.IsSuccess)
        {
            return this.ToNotFoundProblem(NotFoundProblem);
        }
        return Ok(response);
    }
}
