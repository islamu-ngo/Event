using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.DidCustodyType;
using Explore.Application.Features.DidCustodyTypes.Requests.Queries;
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
public class DidCustodyTypeController(IMediator mediator) : ControllerBase
{

    // GET: api/didcustodytype
    [HttpGet(Name = RouteNames.GetDidCustodyTypeOptions)]
    [EndpointSummary("Get all DID Custody Types")]
    [EndpointDescription("Retrieve a list of all DID custody types (Self-Custodied, Custodial, Managed)")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<DidCustodyTypeListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<DidCustodyTypeListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var didCustodyTypes = await mediator.Send(new GetDidCustodyTypeListRequest(), cancellationToken);
        return Ok(didCustodyTypes);
    }

    // GET: api/didcustodytype/{id}
    [HttpGet("{id}", Name = RouteNames.GetDidCustodyTypeOptionById)]
    [EndpointSummary("Get DID Custody Type by ID")]
    [EndpointDescription("Retrieve details of a specific DID custody type")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(DidCustodyTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<DidCustodyTypeDto>> GetById(int id, CancellationToken cancellationToken = default)
    {
        var didCustodyType = await mediator.Send(new GetDidCustodyTypeDetailsRequest { Id = id }, cancellationToken);
        return Ok(didCustodyType);
    }
}
