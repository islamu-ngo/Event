using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/admin/instance/deployment-mode")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class InstanceDeploymentModeController(
    IQueryHandler<GetInstanceDeploymentModeRunbookQuery, InstanceDeploymentModeRunbookDto> runbookQuery,
    ICommandHandler<TransitionInstanceDeploymentModeCommand, BaseCommandResponse<InstanceDeploymentModeTransitionDto>> transitionMode,
    IResourceAssembler<InstanceDeploymentModeRunbookDto, InstanceDeploymentModeRunbookDto> runbookAssembler)
    : EventControllerBase
{
    [HttpGet("", Name = RouteNames.GetInstanceAdminDeploymentModeRunbook)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Deployment Mode Runbook")]
    [EndpointDescription("Returns the Instance Administration runbook for deliberate single-tenant and multi-tenant mode migration.")]
    [ProducesResponseType(typeof(HalResource<InstanceDeploymentModeRunbookDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<InstanceDeploymentModeRunbookDto>>> GetRunbook(
        CancellationToken cancellationToken = default)
    {
        var runbook = await runbookQuery.QueryAsync(new GetInstanceDeploymentModeRunbookQuery(), cancellationToken);
        var resource = await runbookAssembler.ToResource(runbook, HttpContext);

        return Ok(resource);
    }

    [HttpPost("transition", Name = RouteNames.TransitionInstanceAdminDeploymentMode)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Transition Deployment Mode")]
    [EndpointDescription("Runs the Instance Administration deployment-mode runbook after validating typed confirmation and tenant-count preconditions.")]
    [ProducesResponseType(typeof(BaseCommandResponse<InstanceDeploymentModeTransitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<InstanceDeploymentModeTransitionDto>>> Transition(
        [FromBody] InstanceDeploymentModeTransitionRequestDto? dto,
        CancellationToken cancellationToken = default)
    {
        if (dto is null
            || !Enum.TryParse<DeploymentMode>(dto.TargetMode, ignoreCase: false, out var targetMode)
            || !Enum.IsDefined(targetMode))
        {
            var message = "A valid target deployment mode is required.";
            return BadRequest(BaseCommandResponse.Validation<InstanceDeploymentModeTransitionDto>([message], message));
        }

        var response = await transitionMode.ExecuteAsync(
            new TransitionInstanceDeploymentModeCommand(targetMode, dto.Reason, dto.ConfirmationText),
            cancellationToken);

        return this.MapCommandResponse(response);
    }
}
