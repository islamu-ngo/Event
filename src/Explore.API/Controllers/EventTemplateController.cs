using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Features.EventTemplates.Requests.Commands;
using Explore.Application.Features.EventTemplates.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

/// <summary>
/// Event template management API endpoints.
/// All responses include HATEOAS links by default.
/// Send "Prefer: return=minimal" header to strip links.
/// </summary>
[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class EventTemplateController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "eventTemplate",
        "Event template validation failed",
        "Event template update failed.");

    private static readonly ApiNotFoundProblemDescriptor EventTemplateNotFoundProblem = new(
        "Event template not found",
        "Event template not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<EventTemplateDto, EventTemplateListDto> _resourceAssembler;

    public EventTemplateController(
        IMediator mediator,
        IResourceAssembler<EventTemplateDto, EventTemplateListDto> resourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
    }

    /// <summary>
    /// Get event templates with pagination, optionally filtered by event type.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet(Name = RouteNames.GetEventTemplates)]
    [EndpointSummary("Get all EventTemplates")]
    [EndpointDescription("Get a paginated list of event templates for the current tenant. " +
        "Optionally filter by event type. Default page size is 20, max is 100. " +
        "Response includes HATEOAS navigation links (first, prev, next, last). " +
        "Send 'Prefer: return=minimal' header to strip links.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventTemplateListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<HalCollectionResource<EventTemplateListDto>>> GetAll(
        [FromQuery] EventTemplateListQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetEventTemplateListRequest
        {
            EventTypeId = query.EventTypeId,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetEventTemplates,
            additionalRouteValues: new { query.EventTypeId },
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get event template details by ID.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}", Name = RouteNames.GetEventTemplateById)]
    [EndpointSummary("Get EventTemplate Details")]
    [EndpointDescription("Get full details of an event template including its custom property definitions and options. " +
        "Response includes links to related resources.")]
    [ProducesResponseType(typeof(HalResource<EventTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<EventTemplateDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await _mediator.Send(new GetEventTemplateDetailsRequest { Id = id }, cancellationToken);
        if (template == null)
        {
            return this.ToNotFoundProblem(EventTemplateNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(template, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Create a new event template.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateEventTemplate)]
    [EndpointSummary("Create EventTemplate")]
    [EndpointDescription("Create a new event template with optional custom property definitions.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateEventTemplateDto eventTemplate, CancellationToken cancellationToken = default)
    {
        var command = new CreateEventTemplateCommand
        {
            TemplateDto = eventTemplate
        };

        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToQuotaProblemOrBadRequest(response);
        }

        return CreatedAtRoute(
            RouteNames.GetEventTemplateById,
            new { id = response.Id },
            response);
    }

    /// <summary>
    /// Update an event template.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateEventTemplate)]
    [EndpointSummary("Update EventTemplate")]
    [EndpointDescription("Update supplied event template groups; definitions are replaced only when supplied.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id,
        [FromBody] UpdateEventTemplateDto updateDto,
        [FromHeader(Name = "If-Match"), Required] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(UpdateValidationProblem, "If-Match header is required and must contain the current event template concurrency stamp.");
        }

        var command = new UpdateEventTemplateCommand
        {
            TemplateId = id,
            TemplateDto = updateDto,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(EventTemplateNotFoundProblem)
                : this.ToQuotaProblemOrBadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Delete an event template.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteEventTemplate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var command = new DeleteEventTemplateCommand { Id = id };
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }
}
