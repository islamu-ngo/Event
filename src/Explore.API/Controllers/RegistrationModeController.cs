using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.RegistrationMode;
using Explore.Application.Features.RegistrationModes.Requests.Queries;
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
public class RegistrationModeController(IMediator mediator) : ControllerBase
{

    // GET: api/registrationmode
    [HttpGet(Name = RouteNames.GetRegistrationModes)]
    [EndpointSummary("Get all Registration Modes")]
    [EndpointDescription("Retrieve a list of all registration modes (Open, ApprovalRequired, InvitationOnly)")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<RegistrationModeListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<RegistrationModeListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var registrationModes = await mediator.Send(new GetRegistrationModeListRequest(), cancellationToken);
        return Ok(registrationModes);
    }

    // GET: api/registrationmode/{id}
    [HttpGet("{id}", Name = RouteNames.GetRegistrationModeById)]
    [EndpointSummary("Get Registration Mode by ID")]
    [EndpointDescription("Retrieve details of a specific registration mode")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(RegistrationModeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<RegistrationModeDto>> GetById(int id, CancellationToken cancellationToken = default)
    {
        var registrationMode = await mediator.Send(new GetRegistrationModeDetailsRequest { Id = id }, cancellationToken);
        return Ok(registrationMode);
    }
}
