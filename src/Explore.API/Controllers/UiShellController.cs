using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.DTOs.UiShell;
using Explore.Application.Features.UiShell.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiController]
[ApiVersion("0.1")]
[Route("api/ui-shell")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class UiShellController(IMediator mediator) : ControllerBase
{
    [PrivateNoStore]
    [HttpGet("context", Name = RouteNames.GetUiShellContext)]
    [EndpointSummary("Get UI shell context")]
    [EndpointDescription("Returns the authenticated caller's workspace availability, managed actors, settings scopes, deployment mode, and navigation defaults.")]
    [ProducesResponseType(typeof(UiShellContextDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UiShellContextDto>> GetContext(CancellationToken cancellationToken = default)
    {
        UiShellContextDto context = await mediator.Send(new GetUiShellContextRequest(), cancellationToken);
        return Ok(context);
    }
}
