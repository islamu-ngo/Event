using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.PlatformMonetization;
using Explore.Application.Features.PlatformMonetization.Requests.Commands;
using Explore.Application.Features.PlatformMonetization.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/instance/settings/platform-monetization")]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class PlatformMonetizationSettingsController(
    IMediator mediator,
    IResourceAssembler<PlatformMonetizationSettingsDto, PlatformMonetizationSettingsDto> assembler) : ControllerBase
{
    [HttpGet("", Name = RouteNames.GetInstancePlatformMonetizationSettings)]
    [ProducesResponseType(typeof(HalResource<PlatformMonetizationSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<PlatformMonetizationSettingsDto>>> Get(CancellationToken cancellationToken)
    {
        PlatformMonetizationSettingsDto settings = await mediator.Send(new GetPlatformMonetizationSettingsQuery(), cancellationToken);
        var response = new ObjectResult(await assembler.ToResource(settings, HttpContext))
        {
            StatusCode = StatusCodes.Status200OK
        };
        response.ContentTypes.Add(HateoasConstants.HalJsonMediaType);
        return response;
    }

    [HttpPut("", Name = RouteNames.UpdateInstancePlatformMonetizationSettings)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        [FromBody] UpdatePlatformMonetizationSettingsDto settings,
        CancellationToken cancellationToken)
    {
        BaseCommandResponse<Guid> response = await mediator.Send(
            new UpdatePlatformMonetizationSettingsCommand { Settings = settings },
            cancellationToken);
        return Ok(response);
    }
}
