using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

/// <summary>
/// CustomPropertyDefinition management API endpoints.
/// All responses include HATEOAS links by default.
/// Send "Prefer: return=minimal" header to strip links.
/// </summary>
[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class CustomPropertyDefinitionController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "customPropertyDefinition",
        "Custom property definition validation failed",
        "Custom property definition update failed.");

    private static readonly ApiValidationProblemDescriptor PurgeValidationProblem = new(
        "customPropertyDefinition",
        "Custom property definition purge failed",
        "Custom property definition purge failed.");

    private static readonly ApiNotFoundProblemDescriptor PurgeNotFoundProblem = new(
        "Custom property definition not found",
        "Custom-property definition not found.");

    private static readonly ApiNotFoundProblemDescriptor DefinitionNotFoundProblem = new(
        "Custom property definition not found",
        "Custom property definition not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<CustomPropertyDefinitionDto, CustomPropertyDefinitionListDto> _resourceAssembler;

    public CustomPropertyDefinitionController(
        IMediator mediator,
        IResourceAssembler<CustomPropertyDefinitionDto, CustomPropertyDefinitionListDto> resourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
    }

    /// <summary>
    /// Get shared custom-property definitions with pagination.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet(Name = RouteNames.GetCustomPropertyDefinitions)]
    [EndpointSummary("Get all CustomPropertyDefinitions")]
    [EndpointDescription("Get a paginated list of shared custom-property definitions for one entity scope. Default page size is 20, max is 100. " +
        "Response includes HATEOAS navigation links (first, prev, next, last). " +
        "Send 'Prefer: return=minimal' header to strip links.")]
    [ProducesResponseType(typeof(HalCollectionResource<CustomPropertyDefinitionListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<HalCollectionResource<CustomPropertyDefinitionListDto>>> GetAll(
        [FromQuery] CustomPropertyDefinitionListQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetCustomPropertyDefinitionListRequest
        {
            EntityTypeName = query.EntityTypeName,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetCustomPropertyDefinitions,
            additionalRouteValues: new { query.EntityTypeName },
            HttpContext);

        return Ok(halResource);
    }


    /// <summary>
    /// Get shared custom-property definition details by ID.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("{id:guid}", Name = RouteNames.GetCustomPropertyDefinitionById)]
    [EndpointSummary("Get CustomPropertyDefinition Details")]
    [EndpointDescription("Get full details of a custom property definition including its options. " +
        "Response includes links to related resources (events, members).")]
    [ProducesResponseType(typeof(HalResource<CustomPropertyDefinitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<CustomPropertyDefinitionDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var definition = await _mediator.Send(new GetCustomPropertyDefinitionDetailsRequest { Id = id }, cancellationToken);
        if (definition == null)
        {
            return this.ToNotFoundProblem(DefinitionNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(definition, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Create a new custom property definition.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateCustomPropertyDefinition)]
    [EndpointSummary("Create CustomPropertyDefinition")]
    [EndpointDescription("Create a new shared custom-property definition for Organization or Group scope.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateCustomPropertyDefinitionDto customPropertyDefinition, CancellationToken cancellationToken = default)
    {
        var command = new CreateCustomPropertyDefinitionCommand
        {
            DefinitionDto = customPropertyDefinition
        };

        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToQuotaProblemOrBadRequest(response);
        }

        return CreatedAtRoute(
            RouteNames.GetCustomPropertyDefinitionById,
            new { id = response.Id },
            response);
    }

    /// <summary>
    /// Update a shared custom-property definition.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateCustomPropertyDefinition)]
    [EndpointSummary("Update CustomPropertyDefinition")]
    [EndpointDescription("Update supplied groups on a shared custom-property definition; options are replaced only when supplied.")]
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
        [FromBody] UpdateCustomPropertyDefinitionDto updateDto,
        [FromHeader(Name = "If-Match"), Required] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(UpdateValidationProblem, "If-Match header is required and must contain the current custom-property definition concurrency stamp.");
        }

        var command = new UpdateCustomPropertyDefinitionCommand
        {
            DefinitionId = id,
            DefinitionDto = updateDto,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return result.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(PurgeNotFoundProblem)
                : this.ToQuotaProblemOrBadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Delete a shared custom-property definition.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteCustomPropertyDefinition)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var command = new DeleteCustomPropertyDefinitionCommand { Id = id };
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Permanently purge a dependency-free shared custom-property definition.
    /// </summary>
    [Authorize(Policy = ApiAuthorizationPolicies.Admin)]
    [EndpointClassification(EndpointClass.Admin)]
    [HttpDelete("{id:guid}/purge", Name = RouteNames.PurgeCustomPropertyDefinition)]
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
        var result = await _mediator.Send(new PurgeCustomPropertyDefinitionCommand
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
}
