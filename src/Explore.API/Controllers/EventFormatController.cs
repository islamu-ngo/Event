using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.EventFormat;
using Explore.Application.Features.EventFormats.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public class EventFormatController(IMediator mediator) : ControllerBase
{

    // GET: api/eventformat
    [HttpGet(Name = RouteNames.GetEventFormatOptions)]
    [EndpointSummary("Get all Event Formats")]
    [EndpointDescription("Retrieve a list of all event delivery formats (In-person Local, Digital Online, Hybrid)")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<EventFormatListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<EventFormatListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var eventFormats = await mediator.Send(new GetEventFormatListRequest(), cancellationToken);
        return Ok(eventFormats);
    }

    // GET: api/eventformat/{id}
    [HttpGet("{id}", Name = RouteNames.GetEventFormatOptionById)]
    [EndpointSummary("Get Event Format by ID")]
    [EndpointDescription("Retrieve details of a specific event delivery format")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(EventFormatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<EventFormatDto>> GetById(int id, CancellationToken cancellationToken = default)
    {
        var eventFormat = await mediator.Send(new GetEventFormatDetailsRequest(id), cancellationToken);
        return Ok(eventFormat);
    }
}
