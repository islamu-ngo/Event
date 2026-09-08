using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.Features.EventSessionTemplates.Requests.Commands;
using Explore.Application.Features.EventSessionTemplates.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

/// <summary>
/// Event session template management API endpoints.
/// All responses include HATEOAS links by default.
/// Send "Prefer: return=minimal" header to strip links.
/// </summary>
[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class EventSessionTemplateController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "eventSessionTemplate",
        "Event session template validation failed",
        "Event session template creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "eventSessionTemplate",
        "Event session template validation failed",
        "Event session template update failed.");

    private static readonly ApiNotFoundProblemDescriptor EventSessionTemplateNotFoundProblem = new(
        "Event session template not found",
        "Event session template not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<EventSessionTemplateDto, EventSessionTemplateListDto> _resourceAssembler;

    public EventSessionTemplateController(
        IMediator mediator,
        IResourceAssembler<EventSessionTemplateDto, EventSessionTemplateListDto> resourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
    }

    /// <summary>
    /// Get event session templates with pagination, filtered by parent event template.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet(Name = RouteNames.GetEventSessionTemplates)]
    [EndpointSummary("Get all EventSessionTemplates")]
    [EndpointDescription("Get a paginated list of event session templates for a specific event template. " +
        "Default page size is 20, max is 100. " +
        "Response includes HATEOAS navigation links (first, prev, next, last). " +
        "Send 'Prefer: return=minimal' header to strip links.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventSessionTemplateListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<HalCollectionResource<EventSessionTemplateListDto>>> GetAll(
        [FromQuery] EventSessionTemplateListQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetEventSessionTemplateListRequest
        {
            EventTemplateId = query.EventTemplateId,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetEventSessionTemplates,
            additionalRouteValues: new { query.EventTemplateId },
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get event session template details by ID.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}", Name = RouteNames.GetEventSessionTemplateById)]
    [EndpointSummary("Get EventSessionTemplate Details")]
    [EndpointDescription("Get full details of an event session template including its custom property definitions and options. " +
        "Response includes links to related resources.")]
    [ProducesResponseType(typeof(HalResource<EventSessionTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<EventSessionTemplateDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _mediator.Send(new GetEventSessionTemplateDetailsRequest { Id = id }, cancellationToken);
        if (template == null)
        {
            return this.ToNotFoundProblem(EventSessionTemplateNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(template, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Create a new event session template.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateEventSessionTemplate)]
    [EndpointSummary("Create EventSessionTemplate")]
    [EndpointDescription("Create a new event session template with optional custom property definitions.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateEventSessionTemplateDto sessionTemplate, CancellationToken cancellationToken = default)
    {
        var command = new CreateEventSessionTemplateCommand
        {
            SessionTemplateDto = sessionTemplate
        };

        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetEventSessionTemplateById,
            new { id = response.Id },
            response);
    }

    /// <summary>
    /// Update an event session template.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateEventSessionTemplate)]
    [EndpointSummary("Update EventSessionTemplate")]
    [EndpointDescription("Update supplied event session template groups; definitions are replaced only when supplied.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id,
        [FromBody] UpdateEventSessionTemplateDto updateDto,
        [FromHeader(Name = "If-Match"), Required] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(UpdateValidationProblem, "If-Match header is required and must contain the current event session template concurrency stamp.");
        }

        var command = new UpdateEventSessionTemplateCommand
        {
            SessionTemplateId = id,
            SessionTemplateDto = updateDto,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(EventSessionTemplateNotFoundProblem)
                : this.ToCommandValidationProblem(result, UpdateValidationProblem);
        }

        return Ok(result);
    }

    /// <summary>
    /// Delete an event session template.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteEventSessionTemplate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var command = new DeleteEventSessionTemplateCommand { Id = id };
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }
}
