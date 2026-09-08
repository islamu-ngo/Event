using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.EventSessionKind;
using Explore.Application.Features.EventSessionKinds.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public class EventSessionKindController(IMediator mediator) : ControllerBase
{

    // GET: api/eventsessionkind
    [HttpGet(Name = RouteNames.GetEventSessionKinds)]
    [EndpointSummary("Get all Event Session Kinds")]
    [EndpointDescription("Retrieve a list of all event session kinds (Talk, Workshop, Panel, Activity, etc.)")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<EventSessionKindListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<EventSessionKindListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var kinds = await mediator.Send(new GetEventSessionKindListRequest(), cancellationToken);
        return Ok(kinds);
    }
}
