using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.EventStatus;
using Explore.Application.Features.EventStatuses.Requests.Queries;
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
public class EventStatusController(IMediator mediator) : ControllerBase
{
    // GET: api/eventstatus
    [HttpGet(Name = RouteNames.GetEventStatuses)]
    [EndpointSummary("Get all Event Statuses")]
    [EndpointDescription("Retrieve a list of all event lifecycle statuses (Draft, Published, Cancelled, Completed)")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<EventStatusListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<EventStatusListDto>>> GetAll(CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetEventStatusListRequest(), cancellationToken));

    // GET: api/eventstatus/{id}
    [HttpGet("{id}", Name = RouteNames.GetEventStatusById)]
    [EndpointSummary("Get Event Status by ID")]
    [EndpointDescription("Retrieve details of a specific event lifecycle status")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(EventStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<EventStatusDto>> GetById(int id, CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetEventStatusDetailsRequest { Id = id }, cancellationToken));
}
