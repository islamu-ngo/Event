using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.EventType;
using Explore.Application.Features.EventTypes.Requests.Queries;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public class EventTypeController(
    IQueryHandler<GetEventTypeListRequest, List<EventTypeListDto>> eventTypeList) : ControllerBase
{

    [HttpGet(Name = RouteNames.GetEventTypes)]
    [EndpointSummary("Get all Event Types")]
    [EndpointDescription("Get A List of all the Event Type Options")]
    [AllowAnonymous]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<EventTypeListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var eventTypes = await eventTypeList.QueryAsync(new GetEventTypeListRequest(), cancellationToken);
        return Ok(eventTypes);
    }
}
