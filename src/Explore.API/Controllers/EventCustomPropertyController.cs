using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

/// <summary>
/// Event custom property definition and value management API endpoints.
/// All responses include HATEOAS links by default.
/// Send "Prefer: return=minimal" header to strip links.
/// </summary>
[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class EventCustomPropertyController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "eventCustomPropertyDefinition",
        "Event custom property definition validation failed",
        "Event custom property definition update failed.");

    private static readonly ApiValidationProblemDescriptor PurgeValidationProblem = new(
        "eventCustomPropertyDefinition",
        "Event custom property definition purge failed",
        "Event custom property definition purge failed.");

    private static readonly ApiNotFoundProblemDescriptor PurgeNotFoundProblem = new(
        "Event custom property definition not found",
        "Event custom-property definition not found.");

    private static readonly ApiNotFoundProblemDescriptor DefinitionNotFoundProblem = new(
        "Event custom property definition not found",
        "Event custom property definition not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<EventCustomPropertyDefinitionDto, EventCustomPropertyDefinitionListDto> _resourceAssembler;

    public EventCustomPropertyController(
        IMediator mediator,
        IResourceAssembler<EventCustomPropertyDefinitionDto, EventCustomPropertyDefinitionListDto> resourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
    }

    /// <summary>
    /// Get event-local custom property definitions with pagination.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet(Name = RouteNames.GetEventCustomPropertyDefinitions)]
    [EndpointSummary("Get all EventCustomPropertyDefinitions")]
    [EndpointDescription("Get a paginated list of custom property definitions for a specific event. " +
        "Includes both template-instantiated and ad-hoc definitions. " +
        "Default page size is 20, max is 100. " +
        "Response includes HATEOAS navigation links (first, prev, next, last). " +
        "Send 'Prefer: return=minimal' header to strip links.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventCustomPropertyDefinitionListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<HalCollectionResource<EventCustomPropertyDefinitionListDto>>> GetAll(
        [FromQuery] EventCustomPropertyDefinitionListQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetEventCustomPropertyDefinitionListRequest
        {
            EventId = query.EventId,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetEventCustomPropertyDefinitions,
            additionalRouteValues: new { query.EventId },
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get event-local custom property definition details by ID.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("{id:guid}", Name = RouteNames.GetEventCustomPropertyDefinitionById)]
    [EndpointSummary("Get EventCustomPropertyDefinition Details")]
    [EndpointDescription("Get full details of an event-local custom property definition including its options and provenance information. " +
        "Response includes links to related resources.")]
    [ProducesResponseType(typeof(HalResource<EventCustomPropertyDefinitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<EventCustomPropertyDefinitionDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var definition = await _mediator.Send(new GetEventCustomPropertyDefinitionDetailsRequest { Id = id }, cancellationToken);
        if (definition == null)
        {
            return this.ToNotFoundProblem(DefinitionNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(definition, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Create a new ad-hoc event-local custom property definition.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateEventCustomPropertyDefinition)]
    [EndpointSummary("Create EventCustomPropertyDefinition")]
    [EndpointDescription("Create a new ad-hoc custom property definition for a specific event. " +
        "For template-based definitions, use the event creation endpoint with a templateId instead.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateEventCustomPropertyDefinitionDto definition, CancellationToken cancellationToken = default)
    {
        var command = new CreateEventCustomPropertyDefinitionCommand
        {
            DefinitionDto = definition
        };

        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToQuotaProblemOrBadRequest(response);
        }

        return CreatedAtRoute(
            RouteNames.GetEventCustomPropertyDefinitionById,
            new { id = response.Id },
            response);
    }

    /// <summary>
    /// Update an event-local custom property definition.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateEventCustomPropertyDefinition)]
    [EndpointSummary("Update EventCustomPropertyDefinition")]
    [EndpointDescription("Update supplied groups on an event-local custom property definition; options are replaced only when supplied. " +
        "Provenance fields (source template information) are read-only and preserved.")]
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
        [FromBody] UpdateEventCustomPropertyDefinitionDto updateDto,
        [FromHeader(Name = "If-Match"), Required] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(UpdateValidationProblem, "If-Match header is required and must contain the current event custom-property definition concurrency stamp.");
        }

        var command = new UpdateEventCustomPropertyDefinitionCommand
        {
            DefinitionId = id,
            DefinitionDto = updateDto,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(DefinitionNotFoundProblem)
                : this.ToQuotaProblemOrBadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Delete an event-local custom property definition.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteEventCustomPropertyDefinition)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var command = new DeleteEventCustomPropertyDefinitionCommand { Id = id };
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Permanently purge a dependency-free event-local custom property definition.
    /// </summary>
    [Authorize(Policy = ApiAuthorizationPolicies.Admin)]
    [EndpointClassification(EndpointClass.Admin)]
    [HttpDelete("{id:guid}/purge", Name = RouteNames.PurgeEventCustomPropertyDefinition)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<CustomPropertyPurgeResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<CustomPropertyPurgeResultDto>>> Purge(
        Guid id,
        [FromBody] PurgeCustomPropertyDefinitionDto purgeDto,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new PurgeEventCustomPropertyDefinitionCommand
        {
            Id = id,
            Reason = purgeDto.Reason
        }, cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result);
        }

        return result.FailureCode == FailureCodes.NotFound
            ? this.ToNotFoundProblem(PurgeNotFoundProblem)
            : this.ToCommandValidationProblem(result, PurgeValidationProblem);
    }

    /// <summary>
    /// Get all custom property values for an event.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("values", Name = RouteNames.GetEventCustomPropertyValues)]
    [EndpointSummary("Get EventCustomPropertyValues")]
    [EndpointDescription("Get all custom property values for a specific event across all definitions.")]
    [ProducesResponseType(typeof(List<EventCustomPropertyValueDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<List<EventCustomPropertyValueDto>>> GetValues(
        [FromQuery] Guid eventId, CancellationToken cancellationToken = default)
    {
        var values = await _mediator.Send(new GetEventCustomPropertyValuesRequest
        {
            EventId = eventId
        }, cancellationToken);

        return Ok(values);
    }

    /// <summary>
    /// Set a single custom property value for an event.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPut("value", Name = RouteNames.SetEventCustomPropertyValue)]
    [EndpointSummary("Set EventCustomPropertyValue")]
    [EndpointDescription("Set or update a single custom property value for an event. " +
        "Uses upsert semantics based on definition ID, event ID, and ordinal.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SetValue(
        [FromBody] SetEventCustomPropertyValueDto valueDto, CancellationToken cancellationToken = default)
    {
        var command = new SetEventCustomPropertyValueCommand
        {
            ValueDto = valueDto
        };

        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToQuotaProblemOrBadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Set multiple values for a multi-value custom property definition.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPut("values", Name = RouteNames.SetEventCustomPropertyMultiValues)]
    [EndpointSummary("Set EventCustomPropertyMultiValues")]
    [EndpointDescription("Atomically replace all values for a multi-value custom property definition. " +
        "All existing values for the definition+event combination are removed and replaced.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SetMultiValues(
        [FromBody] SetEventCustomPropertyMultiValuesDto multiValuesDto, CancellationToken cancellationToken = default)
    {
        var command = new SetEventCustomPropertyMultiValuesCommand
        {
            DefinitionId = multiValuesDto.DefinitionId,
            EventId = multiValuesDto.EventId,
            Values = multiValuesDto.Values
        };

        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToQuotaProblemOrBadRequest(response);
        }

        return Ok(response);
    }
}
