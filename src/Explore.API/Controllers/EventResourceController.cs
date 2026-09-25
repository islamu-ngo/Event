using System.Collections.Immutable;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.API.Models;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Features.EventResources;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiController]
[ApiVersion("0.1")]
[Route("api")]
[Tags("EventResources")]
[EndpointClassification(EndpointClass.Public)]
[PrivateNoStore]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class EventResourceController(
    IQueryHandler<GetEventResourceAudienceDetailQuery, EventResourceAudienceDetailResult> detail,
    IQueryHandler<ListEventResourcesQuery, EventResourceAudiencePageResult> list,
    IQueryHandler<AuthorizeEventResourceAudienceDisclosureQuery, EventResourceAudienceFailure> disclosure,
    IResourceAssembler<EventResourceAudienceDetailDto, EventResourceAudienceDetailDto> assembler,
    ITenantContext tenant) : ControllerBase
{
    [HttpGet("eventresource/{id:guid}", Name = RouteNames.GetEventResourceAudienceDetail)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HalResource<EventResourceAudienceDetailDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalResource<EventResourceAudienceDetailDto>>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await detail.QueryAsync(new(id), cancellationToken);
        if (result.Failure != EventResourceAudienceFailure.None || result.Value is not { } value || result.Proof is not { } proof)
            return ReadFailure(result.Failure);
        var representation = await assembler.ToResource(value, HttpContext);
        var failure = await disclosure.QueryAsync(new(proof), cancellationToken);
        return failure == EventResourceAudienceFailure.None ? Ok(representation) : ReadFailure(failure);
    }

    [HttpGet("event/{eventId:guid}/resources", Name = RouteNames.ListEventResources)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(EventResourceAudiencePageResource), StatusCodes.Status200OK)]
    public async Task<ActionResult<EventResourceAudiencePageResource>> List(
        Guid eventId, [FromQuery] int pageSize = 20, [FromQuery] string? cursor = null, CancellationToken cancellationToken = default)
    {
        var result = await list.QueryAsync(new(eventId, pageSize, cursor), cancellationToken);
        if (result.Failure != EventResourceAudienceFailure.None || result.Value is not { } value || result.Proof is not { } proof)
            return ReadFailure(result.Failure);
        var representation = await assembler.ToCollectionResource(value.Items, RouteNames.ListEventResources,
            new EventMaterialAudienceCollectionContext(tenant.TenantId, eventId, pageSize, cursor, value.NextCursor), HttpContext);
        var failure = await disclosure.QueryAsync(new(proof), cancellationToken);
        return failure == EventResourceAudienceFailure.None
            ? Ok(new EventResourceAudiencePageResource(value.NextCursor,
                representation.Links.ToImmutableDictionary(), representation.Embedded))
            : ReadFailure(failure);
    }

    private ActionResult ReadFailure(EventResourceAudienceFailure failure) => failure switch
    {
        EventResourceAudienceFailure.InvalidRequest => this.ToValidationProblem(
            new("eventResource", "Invalid resource query", "The resource query is invalid."), "Refresh the resource query and try again."),
        EventResourceAudienceFailure.AuthenticationRequired => this.ToAuthenticationRequiredProblem(),
        EventResourceAudienceFailure.Forbidden => this.ToForbiddenProblem(),
        EventResourceAudienceFailure.NotFound => this.ToNotFoundProblem(new("Resource not found", "The requested resource was not found.")),
        _ => this.ToServiceUnavailableProblem("Resource authority unavailable",
            "Current resource authority could not be established.", EventResourceManagementFailureCodes.Unavailable)
    };
}
