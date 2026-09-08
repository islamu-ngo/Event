using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventRoleAssignment;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class EventTeamController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IAdminContext _adminContext;
    private readonly ITenantContext _tenantContext;
    private readonly IResourceAssembler<EventTeamMemberDto, EventTeamMemberDto> _resourceAssembler;

    public EventTeamController(
        IMediator mediator,
        IAdminContext adminContext,
        ITenantContext tenantContext,
        IResourceAssembler<EventTeamMemberDto, EventTeamMemberDto> resourceAssembler)
    {
        _mediator = mediator;
        _adminContext = adminContext;
        _tenantContext = tenantContext;
        _resourceAssembler = resourceAssembler;
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("by-event/{eventId:guid}", Name = RouteNames.GetEventTeam)]
    [EndpointSummary("Get Event Team")]
    [EndpointDescription("List all team members for an event with their role assignments.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventTeamMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<EventTeamMemberDto>>> GetTeam(
        Guid eventId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetEventTeamListRequest
        {
            TenantId = _tenantContext.TenantId,
            EventId = eventId,
            IncludeInactive = includeInactive
        }, cancellationToken);

        var resource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetEventTeam,
            new EventTeamCollectionAuthorizationContext(_tenantContext.TenantId, eventId),
            HttpContext);

        return Ok(resource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("my-permissions/{eventId:guid}", Name = RouteNames.GetCurrentUserEventPermissions)]
    [EndpointSummary("Get My Event Permissions")]
    [EndpointDescription("Get the current user's effective event permissions for HAL affordance gating.")]
    [ProducesResponseType(typeof(CurrentUserEventPermissionsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentUserEventPermissionsDto>> GetMyPermissions(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveRequiredUserIdAsync(cancellationToken);
        if (userId is null)
            return this.ToAuthenticationRequiredProblem();

        var result = await _mediator.Send(new GetCurrentUserEventPermissionsRequest
        {
            TenantId = _tenantContext.TenantId,
            EventId = eventId,
            UserId = userId.Value
        }, cancellationToken);

        return Ok(result);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("assignable-presets/{eventId:guid}", Name = RouteNames.GetEventTeamAssignablePresets)]
    [EndpointSummary("Get Assignable Event Role Presets")]
    [EndpointDescription("Get the event role presets the current user is allowed to assign.")]
    [ProducesResponseType(typeof(List<EventRolePresetDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<EventRolePresetDto>>> GetAssignablePresets(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var userId = await ResolveRequiredUserIdAsync(cancellationToken);
        if (userId is null)
            return this.ToAuthenticationRequiredProblem();

        var result = await _mediator.Send(new GetAssignableEventRolePresetsRequest
        {
            TenantId = _tenantContext.TenantId,
            EventId = eventId,
            AssignerUserId = userId.Value
        }, cancellationToken);

        return Ok(result);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("by-event/{eventId:guid}/assignments", Name = RouteNames.AssignEventRole)]
    [EndpointSummary("Assign Event Role")]
    [EndpointDescription("Assign an event-scoped operational role to an existing user.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> AssignRole(
        Guid eventId,
        [FromBody] AssignEventTeamRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var actorUserId = await ResolveRequiredUserIdAsync(cancellationToken);
        if (actorUserId is null)
            return this.ToAuthenticationRequiredProblem();

        var result = await _mediator.Send(new AssignEventRoleByEmailCommand
        {
            TenantId = _tenantContext.TenantId,
            EventId = eventId,
            TargetUserEmail = request.UserEmail,
            RoleId = request.RoleId,
            ActorUserId = actorUserId.Value
        }, cancellationToken);

        return Ok(result);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("by-event/{eventId:guid}/assignments/{assignmentId:guid}", Name = RouteNames.RevokeEventRole)]
    [EndpointSummary("Revoke Event Role")]
    [EndpointDescription("Revoke an event-scoped operational role assignment.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RevokeRole(
        Guid eventId,
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        var actorUserId = await ResolveRequiredUserIdAsync(cancellationToken);
        if (actorUserId is null)
            return this.ToAuthenticationRequiredProblem();

        var result = await _mediator.Send(new RevokeEventRoleAssignmentCommand
        {
            TenantId = _tenantContext.TenantId,
            EventId = eventId,
            AssignmentId = assignmentId,
            ActorUserId = actorUserId.Value
        }, cancellationToken);

        return Ok(result);
    }

    private Task<Guid?> ResolveRequiredUserIdAsync(CancellationToken cancellationToken) =>
        _adminContext.ResolveUserIdAsync(cancellationToken);
}

public sealed class AssignEventTeamRoleRequest
{
    public string UserEmail { get; set; } = string.Empty;
    public int RoleId { get; set; }
}
