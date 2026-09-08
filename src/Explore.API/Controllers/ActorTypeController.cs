using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.ActorType;
using Explore.Application.Features.ActorTypes.Requests.Queries;
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
public class ActorTypeController(IMediator mediator) : ControllerBase
{
    // GET: api/actortype
    [HttpGet(Name = RouteNames.GetActorTypes)]
    [EndpointSummary("Get all Actor Types")]
    [EndpointDescription("Retrieve a list of all actor types (User, Organization, Service, Bot)")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<ActorTypeListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<ActorTypeListDto>>> GetAll(CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetActorTypeListRequest(), cancellationToken));

    // GET: api/actortype/{id}
    [HttpGet("{id}", Name = RouteNames.GetActorTypeById)]
    [EndpointSummary("Get Actor Type by ID")]
    [EndpointDescription("Retrieve details of a specific actor type")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ActorTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<ActorTypeDto>> GetById(int id, CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetActorTypeDetailsRequest { Id = id }, cancellationToken));
}
