using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.AudienceAge;
using Explore.Application.Features.AudienceAges.Requests.Queries;
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
public class AudienceAgeController(IMediator mediator) : ControllerBase
{
    // GET: api/audienceage
    [HttpGet(Name = RouteNames.GetAudienceAgeOptions)]
    [EndpointSummary("Get all Audience Age groups")]
    [EndpointDescription("Retrieve a list of all audience age groups (Children, Youth, Adults, Seniors, All Ages)")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<AudienceAgeListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<AudienceAgeListDto>>> GetAll(CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetAudienceAgeListRequest(), cancellationToken));

    // GET: api/audienceage/{id}
    [HttpGet("{id}", Name = RouteNames.GetAudienceAgeOptionById)]
    [EndpointSummary("Get Audience Age group by ID")]
    [EndpointDescription("Retrieve details of a specific audience age group")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AudienceAgeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<AudienceAgeDto>> GetById(int id, CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetAudienceAgeDetailsRequest { Id = id }, cancellationToken));
}
