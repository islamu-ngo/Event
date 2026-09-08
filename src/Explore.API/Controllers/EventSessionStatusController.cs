using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.EventSessionStatus;
using Explore.Application.Features.EventSessionStatuses.Requests.Queries;
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
public class EventSessionStatusController(IMediator mediator) : ControllerBase
{

    [HttpGet(Name = RouteNames.GetEventSessionStatuses)]
    [EndpointSummary("Get all Event Session Statuses")]
    [EndpointDescription("Returns the complete list of event session lifecycle status lookup values (draft, submitted, under review, approved, published, rejected, cancelled, archived).")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<EventSessionStatusListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<EventSessionStatusListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var statuses = await mediator.Send(new GetEventSessionStatusListRequest(), cancellationToken);
        return Ok(statuses);
    }

    [HttpGet("{id}", Name = RouteNames.GetEventSessionStatusById)]
    [EndpointSummary("Get Event Session Status by ID")]
    [EndpointDescription("Returns a single event session lifecycle status lookup value by its integer identifier.")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(EventSessionStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<EventSessionStatusDto>> GetById(int id, CancellationToken cancellationToken = default)
    {
        var status = await mediator.Send(new GetEventSessionStatusDetailsRequest { Id = id }, cancellationToken);
        return Ok(status);
    }
}
