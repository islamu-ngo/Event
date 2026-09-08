using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.AudienceGender;
using Explore.Application.Features.AudienceGenders.Requests.Queries;
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
public class AudienceGenderController(IMediator mediator) : ControllerBase
{

    // GET: api/audiencegender
    [HttpGet(Name = RouteNames.GetAudienceGenderOptions)]
    [EndpointSummary("Get all Audience Gender types")]
    [EndpointDescription("Retrieve a list of all audience gender types (Men-only, Women-only, Mixed, Family)")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<AudienceGenderListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<AudienceGenderListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var audienceGenders = await mediator.Send(new GetAudienceGenderListRequest(), cancellationToken);
        return Ok(audienceGenders);
    }

    // GET: api/audiencegender/{id}
    [HttpGet("{id}", Name = RouteNames.GetAudienceGenderOptionById)]
    [EndpointSummary("Get Audience Gender type by ID")]
    [EndpointDescription("Retrieve details of a specific audience gender type")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AudienceGenderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<AudienceGenderDto>> GetById(int id, CancellationToken cancellationToken = default)
    {
        var audienceGender = await mediator.Send(new GetAudienceGenderDetailsRequest { Id = id }, cancellationToken);

        return Ok(audienceGender);
    }
}
