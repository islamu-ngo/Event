using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.EventType;
using Explore.Application.Features.EventTypes.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public class EventTypeController(IMediator mediator) : ControllerBase
{

    [HttpGet(Name = RouteNames.GetEventTypes)]
    [EndpointSummary("Get all Event Types")]
    [EndpointDescription("Get A List of all the Event Type Options")]
    [AllowAnonymous]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<EventTypeListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var eventTypes = await mediator.Send(new GetEventTypeListRequest { FullName = string.Empty }, cancellationToken);
        return Ok(eventTypes);
    }
}
