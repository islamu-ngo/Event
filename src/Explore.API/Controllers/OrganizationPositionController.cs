using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.OrganizationPosition;
using Explore.Application.Features.OrganizationPositions.Requests.Queries;
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
public class OrganizationPositionController(IMediator mediator) : ControllerBase
{

    // GET: api/organizationposition
    [HttpGet(Name = RouteNames.GetOrganizationPositions)]
    [EndpointSummary("Get all Organization Positions")]
    [EndpointDescription("Retrieve a list of all organization positions (President, Secretary, Member)")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<OrganizationPositionListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<OrganizationPositionListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var organizationPositions = await mediator.Send(new GetOrganizationPositionListRequest(), cancellationToken);
        return Ok(organizationPositions);
    }

    // GET: api/organizationposition/{id}
    [HttpGet("{id}", Name = RouteNames.GetOrganizationPositionById)]
    [EndpointSummary("Get Organization Position by ID")]
    [EndpointDescription("Retrieve details of a specific organization position")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(OrganizationPositionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<OrganizationPositionDto>> GetById(int id, CancellationToken cancellationToken = default)
    {
        var organizationPosition = await mediator.Send(new GetOrganizationPositionDetailsRequest { Id = id }, cancellationToken);
        return Ok(organizationPosition);
    }
}
