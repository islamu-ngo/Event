using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Deployment;
using Explore.Application.Features.Deployment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/deployment/ticketing-capabilities")]
public sealed class TicketingDeploymentCapabilitiesController(
    IMediator mediator) :
    ControllerBase
{
    [HttpGet("", Name = RouteNames.GetTicketingDeploymentCapabilities)]
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [ProducesResponseType(
        typeof(TicketingDeploymentCapabilityMatrixDto),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<TicketingDeploymentCapabilityMatrixDto>> Get(
        CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new GetTicketingDeploymentCapabilitiesQuery(),
            cancellationToken));
}
