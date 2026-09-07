using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Authentication;
using Explore.Application.DTOs.Group;
using Explore.Application.DTOs.Notification;
using Explore.Application.Features.Groups.Requests.Commands;
using Explore.Application.Features.Groups.Requests.Queries;
using Explore.Application.Features.Notifications.Requests.Commands;
using Explore.Application.Features.Notifications.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

/// <summary>
/// Group management API endpoints.
/// All responses include HATEOAS links by default.
/// Send "Prefer: return=minimal" header to strip links.
/// </summary>
[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class GroupController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "group",
        "Group validation failed",
        "Group creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "group",
        "Group validation failed",
        "Group update failed.");

    private static readonly ApiValidationProblemDescriptor PreferenceValidationProblem = new(
        "groupNotificationPreferences",
        "Group notification preference validation failed",
        "Group notification preference update failed.");

    private static readonly ApiValidationProblemDescriptor ApprovalValidationProblem = new(
        "group",
        "Group validation failed",
        "Group approval status update failed.");

    private static readonly ApiValidationProblemDescriptor DeleteValidationProblem = new(
        "group",
        "Group validation failed",
        "Group deletion failed.");

    private static readonly ApiNotFoundProblemDescriptor GroupNotFoundProblem = new(
        "Group not found",
        "Group not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<GroupDto, GroupListDto> _resourceAssembler;
    private readonly IResourceAssembler<NotificationPreferenceMatrixDto> _preferenceAssembler;

    public GroupController(
        IMediator mediator,
        IResourceAssembler<GroupDto, GroupListDto> resourceAssembler,
        IResourceAssembler<NotificationPreferenceMatrixDto> preferenceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
        _preferenceAssembler = preferenceAssembler;
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet(Name = RouteNames.GetGroups)]
    [EndpointSummary("Get all Groups")]
    [EndpointDescription("Get a paginated list of all Groups. Default page size is 20, max is 100.")]
    [ProducesResponseType(typeof(HalCollectionResource<GroupListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<HalCollectionResource<GroupListDto>>> GetAll(
        [FromQuery] PaginationQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetGroupListRequest
        {
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetGroups,
            additionalRouteValues: null,
            HttpContext);

        return Ok(halResource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("my", Name = RouteNames.GetMyGroups)]
    [EndpointSummary("Get my Groups")]
    [EndpointDescription("Get a paginated list of groups where the current user is a member.")]
    [ProducesResponseType(typeof(HalCollectionResource<GroupListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HalCollectionResource<GroupListDto>>> GetMyGroups(
        [FromQuery] PaginationQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId?.ToString();
        if (string.IsNullOrEmpty(userId))
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var result = await _mediator.Send(new GetMyGroupsRequest
        {
            UserId = userId,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetMyGroups,
            additionalRouteValues: null,
            HttpContext);

        return Ok(halResource);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}", Name = RouteNames.GetGroupById)]
    [EndpointSummary("Get Group Details")]
    [EndpointDescription("Get full details of a group.")]
    [ProducesResponseType(typeof(HalResource<GroupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<GroupDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var group = await _mediator.Send(new GetGroupDetailsRequest { Id = id }, cancellationToken);

        if (group == null)
        {
            return this.ToNotFoundProblem(GroupNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(group, HttpContext);
        return Ok(halResource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("{id:guid}/notification-preferences", Name = RouteNames.GetGroupNotificationPreferences)]
    [EndpointSummary("Get Group Notification Preferences")]
    [EndpointDescription("Get the effective notification preference matrix for a group scope.")]
    [ProducesResponseType(typeof(HalResource<NotificationPreferenceMatrixDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<NotificationPreferenceMatrixDto>>> GetNotificationPreferences(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var matrix = await _mediator.Send(new GetGroupNotificationPreferenceMatrixQuery
        {
            GroupId = id
        }, cancellationToken);

        var halResource = await _preferenceAssembler.ToResource(matrix, HttpContext);
        return Ok(halResource);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}/notification-preferences", Name = RouteNames.UpdateGroupNotificationPreferences)]
    [EndpointSummary("Update Group Notification Preferences")]
    [EndpointDescription("Patch supplied group-scoped notification preference cells while preserving omitted cells.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateNotificationPreferences(
        Guid id,
        [FromBody] UpdateNotificationPreferenceMatrixDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new UpdateGroupNotificationPreferenceMatrixCommand
        {
            GroupId = id,
            Cells = request.Cells
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, PreferenceValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPut("{id:guid}/notification-preferences/mute", Name = RouteNames.SetGroupNotificationPreferenceMute)]
    [EndpointSummary("Set Group Notification Preference Mute")]
    [EndpointDescription("Set group-scoped global mute for non-essential notification preferences.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SetNotificationPreferenceMute(
        Guid id,
        [FromBody] SetNotificationPreferenceMuteDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new SetGroupNotificationPreferenceMuteCommand
        {
            GroupId = id,
            IsMuted = request.IsMuted
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, PreferenceValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateGroup)]
    [EndpointSummary("Create Group")]
    [EndpointDescription("Create a new group. The authenticated user becomes the creator.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateGroupDto group, CancellationToken cancellationToken = default)
    {
        var userId = await _mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem(
                detail: "The authenticated principal could not be resolved to an application user.");
        }

        var response = await _mediator.Send(new CreateGroupCommand
        {
            GroupDto = group,
            CreatorUserId = userId.Value
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return CreatedAtRoute(RouteNames.GetGroupById, new { id = response.Id }, response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateGroup)]
    [EndpointSummary("Update Group")]
    [EndpointDescription("Partially update an existing group. Route ID is authoritative and If-Match must contain the current group concurrency stamp.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        Guid id,
        [FromBody] UpdateGroupDto updateDto,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId?.ToString();
        if (string.IsNullOrEmpty(userId))
        {
            return this.ToAuthenticationRequiredProblem();
        }

        if (!TryParseConcurrencyStamp(ifMatch, out var expectedConcurrencyStamp))
        {
            return this.ToValidationProblem(
                UpdateValidationProblem,
                "If-Match header is required and must contain the current group concurrency stamp.");
        }

        var command = new UpdateGroupCommand
        {
            GroupId = id,
            UserId = userId,
            ExpectedConcurrencyStamp = expectedConcurrencyStamp,
            UpdateGroupDto = updateDto
        };

        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(GroupNotFoundProblem)
                : this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPut("{id:guid}/approval-status", Name = RouteNames.UpdateGroupApprovalStatus)]
    [EndpointSummary("Update Group Approval Status")]
    [EndpointDescription("Update the approval status of a group. Requires group update authorization.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateApprovalStatus(
        Guid id,
        [FromBody] UpdateGroupApprovalStatusDto approvalStatus,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new UpdateGroupApprovalStatusCommand
        {
            Id = id,
            GroupApprovalStatusDto = approvalStatus
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, ApprovalValidationProblem);
        }

        return Ok(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteGroup)]
    [EndpointSummary("Delete Group")]
    [EndpointDescription("Delete a group. User must have group deletion permission.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId?.ToString();
        if (string.IsNullOrEmpty(userId))
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var response = await _mediator.Send(new DeleteGroupCommand
        {
            Id = id,
            UserId = userId
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, DeleteValidationProblem);
        }

        return Ok(response);
    }

}
