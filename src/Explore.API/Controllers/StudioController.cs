using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Studio;
using Explore.Application.Features.Studio.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiController]
[ApiVersion("0.1")]
[Route("api/studio")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class StudioController(
    IMediator mediator,
    IResourceAssembler<StudioContextDto, StudioContextDto> assembler) : ControllerBase
{
    [HttpGet("context", Name = RouteNames.GetStudioContext)]
    [PrivateNoStore]
    [EndpointSummary("Get Studio context")]
    [EndpointDescription("Returns private HAL navigation affordances for the authenticated actor context.")]
    [ProducesResponseType(typeof(HalResource<StudioContextDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<StudioContextDto>>> GetContext(
        [FromQuery] Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        StudioContextDto context = await mediator.Send(new GetStudioContextQuery(actorId), cancellationToken);
        var result = new ObjectResult(await assembler.ToResource(context, HttpContext))
        {
            StatusCode = StatusCodes.Status200OK
        };
        result.ContentTypes.Add(HateoasConstants.HalJsonMediaType);
        return result;
    }
}
