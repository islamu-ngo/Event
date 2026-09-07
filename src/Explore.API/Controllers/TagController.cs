using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Tag;
using Explore.Application.Features.Tags.Requests.Commands;
using Explore.Application.Features.Tags.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

/// <summary>
/// Tag management API endpoints.
/// All responses include HATEOAS links by default.
/// Send "Prefer: return=minimal" header to strip links.
/// </summary>
[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class TagController : ControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "tag",
        "Tag validation failed",
        "Tag creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "tag",
        "Tag validation failed",
        "Tag update failed.");

    private static readonly ApiNotFoundProblemDescriptor TagNotFoundProblem = new(
        "Tag not found",
        "Tag not found.");

    private readonly IMediator _mediator;
    private readonly ILogger<TagController> _logger;
    private readonly IResourceAssembler<TagDto, TagListDto> _resourceAssembler;
    private readonly ITenantContext _tenantContext;

    public TagController(
        IMediator mediator,
        ILogger<TagController> logger,
        IResourceAssembler<TagDto, TagListDto> resourceAssembler,
        ITenantContext tenantContext)
    {
        _mediator = mediator;
        _logger = logger;
        _resourceAssembler = resourceAssembler;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Get all tags with pagination.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet(Name = RouteNames.GetTags)]
    [EndpointSummary("Get all Tags")]
    [EndpointDescription("Retrieve a paginated list of all tags for events. " +
        "Default page size is 20, max is 100. " +
        "Response includes HATEOAS navigation links. " +
        "Send 'Prefer: return=minimal' header to strip links.")]
    [ProducesResponseType(typeof(HalCollectionResource<TagListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<HalCollectionResource<TagListDto>>> GetAll(
        [FromQuery] PaginationQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetTagListRequest
        {
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetTags,
            additionalRouteValues: null,
            HttpContext);

        return Ok(halResource);
    }

    /// <summary>
    /// Get tag details by ID.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}", Name = RouteNames.GetTagById)]
    [EndpointSummary("Get Tag Details")]
    [EndpointDescription("Get detailed information about a specific tag. " +
        "Response includes links to events with this tag.")]
    [ProducesResponseType(typeof(HalResource<TagDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<TagDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var tag = await _mediator.Send(new GetTagDetailsRequest { Id = id }, cancellationToken);
        if (tag == null)
        {
            return this.ToNotFoundProblem(TagNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(tag, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Create a new tag.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateTag)]
    [EndpointSummary("Create Tag")]
    [EndpointDescription("Create a new tag for categorizing events.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateTagDto tag, CancellationToken cancellationToken = default)
    {
        var command = new CreateTagCommand { TagDto = tag, TenantId = _tenantContext.TenantId };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return CreatedAtRoute(
            RouteNames.GetTagById,
            new { id = response.Id },
            response);
    }

    /// <summary>
    /// Partially update an existing tag.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateTag)]
    [EndpointSummary("Update Tag")]
    [EndpointDescription("Partially update an existing tag. The route ID and resolved tenant are authoritative.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(Guid id, [FromBody] UpdateTagDto tag, CancellationToken cancellationToken = default)
    {
        var command = new UpdateTagCommand
        {
            TagId = id,
            TenantId = _tenantContext.TenantId,
            Update = tag
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(TagNotFoundProblem)
                : this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    /// <summary>
    /// Delete a tag.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteTag)]
    [EndpointSummary("Delete Tag")]
    [EndpointDescription("Delete a tag. Events using this tag will be unlinked.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var command = new DeleteTagCommand { Id = id };
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }
}
