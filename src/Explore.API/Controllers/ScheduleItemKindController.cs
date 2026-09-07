using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.ScheduleItemKind;
using Explore.Application.Features.ScheduleItemKinds.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public class ScheduleItemKindController(IMediator mediator) : ControllerBase
{

    // GET: api/scheduleitemkind
    [HttpGet(Name = RouteNames.GetScheduleItemKinds)]
    [EndpointSummary("Get all Schedule Item Kinds")]
    [EndpointDescription("Retrieve a list of all schedule item kinds (Break, Ceremony, Keynote, Panel, etc.)")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<ScheduleItemKindListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<ScheduleItemKindListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var kinds = await mediator.Send(new GetScheduleItemKindListRequest(), cancellationToken);
        return Ok(kinds);
    }
}
