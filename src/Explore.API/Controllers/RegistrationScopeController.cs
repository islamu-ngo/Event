using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.RegistrationScope;
using Explore.Application.Features.RegistrationScopes.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public class RegistrationScopeController(IMediator mediator) : ControllerBase
{

    // GET: api/registrationscope
    [HttpGet(Name = RouteNames.GetRegistrationScopes)]
    [EndpointSummary("Get all Registration Scopes")]
    [EndpointDescription("Retrieve a list of all registration scopes (Event, Day, SessionSelection)")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<RegistrationScopeListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "LookupData")]
    public async Task<ActionResult<List<RegistrationScopeListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var scopes = await mediator.Send(new GetRegistrationScopeListRequest(), cancellationToken);
        return Ok(scopes);
    }
}
