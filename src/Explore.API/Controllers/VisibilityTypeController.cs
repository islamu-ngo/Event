using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.VisibilityType;
using Explore.Application.Features.VisibilityTypes.Requests.Queries;
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
public class VisibilityTypeController(IMediator mediator) : ControllerBase
{

    // GET: api/visibilitytype
    [HttpGet(Name = RouteNames.GetVisibilityTypes)]
    [EndpointSummary("Get all Visibility Types")]
    [EndpointDescription("Retrieve a list of all event visibility types (Public, Private, Unlisted)")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<VisibilityTypeListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<VisibilityTypeListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var visibilityTypes = await mediator.Send(new GetVisibilityTypeListRequest(), cancellationToken);
        return Ok(visibilityTypes);
    }

    // GET: api/visibilitytype/{id}
    [HttpGet("{id}", Name = RouteNames.GetVisibilityTypeById)]
    [EndpointSummary("Get Visibility Type by ID")]
    [EndpointDescription("Retrieve details of a specific event visibility type")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(VisibilityTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<VisibilityTypeDto>> GetById(int id, CancellationToken cancellationToken = default)
    {
        var visibilityType = await mediator.Send(new GetVisibilityTypeDetailsRequest { Id = id }, cancellationToken);
        return Ok(visibilityType);
    }
}
