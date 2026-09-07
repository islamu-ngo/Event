using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/admin/control-plane/deployment-mode")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class ControlPlaneDeploymentModeController(
    IMediator mediator,
    IResourceAssembler<ControlPlaneDeploymentModeRunbookDto, ControlPlaneDeploymentModeRunbookDto> runbookAssembler)
    : EventControllerBase
{
    [HttpGet("", Name = RouteNames.GetControlPlaneDeploymentModeRunbook)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Deployment Mode Runbook")]
    [EndpointDescription("Returns the Control Plane runbook for deliberate single-tenant and multi-tenant mode migration.")]
    [ProducesResponseType(typeof(HalResource<ControlPlaneDeploymentModeRunbookDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<ControlPlaneDeploymentModeRunbookDto>>> GetRunbook(
        CancellationToken cancellationToken = default)
    {
        var runbook = await mediator.Send(new GetControlPlaneDeploymentModeRunbookQuery(), cancellationToken);
        var resource = await runbookAssembler.ToResource(runbook, HttpContext);

        return Ok(resource);
    }

    [HttpPost("transition", Name = RouteNames.TransitionControlPlaneDeploymentMode)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Transition Deployment Mode")]
    [EndpointDescription("Runs the Control Plane deployment-mode runbook after validating typed confirmation and tenant-count preconditions.")]
    [ProducesResponseType(typeof(BaseCommandResponse<ControlPlaneDeploymentModeTransitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<ControlPlaneDeploymentModeTransitionDto>>> Transition(
        [FromBody] ControlPlaneDeploymentModeTransitionRequestDto? dto,
        CancellationToken cancellationToken = default)
    {
        if (dto is null
            || !Enum.TryParse<DeploymentMode>(dto.TargetMode, ignoreCase: false, out var targetMode)
            || !Enum.IsDefined(targetMode))
        {
            var message = "A valid target deployment mode is required.";
            return BadRequest(BaseCommandResponse.Validation<ControlPlaneDeploymentModeTransitionDto>([message], message));
        }

        var response = await mediator.Send(
            new TransitionControlPlaneDeploymentModeCommand(targetMode, dto.Reason, dto.ConfirmationText),
            cancellationToken);

        return this.MapCommandResponse(response);
    }
}
