using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.Features.GroupMembers.Requests.Commands;
using Explore.Application.Features.GroupMembers.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Authorize]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class GroupMemberController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor AddValidationProblem = new(
        "groupMember",
        "Group member validation failed",
        "Group member creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "groupMember",
        "Group member validation failed",
        "Group member update failed.");

    private static readonly ApiValidationProblemDescriptor DeleteValidationProblem = new(
        "groupMember",
        "Group member validation failed",
        "Group member deletion failed.");

    private static readonly ApiNotFoundProblemDescriptor GroupMemberNotFoundProblem = new(
        "Group member not found",
        "Group member not found.");

    private readonly ICommandHandler<AddGroupMemberCommand, BaseCommandResponse<Guid>> _addMember;
    private readonly ICommandHandler<UpdateGroupMemberRoleCommand, BaseCommandResponse<Guid>> _updateRole;
    private readonly ICommandHandler<DeleteGroupMemberCommand, BaseCommandResponse<Guid>> _deleteMember;
    private readonly IQueryHandler<GetGroupMembersRequest, List<GroupMemberDto>> _members;
    private readonly IQueryHandler<GetGroupMemberDetailsRequest, GroupMemberDto?> _memberDetails;
    private readonly IResourceAssembler<GroupMemberDto, GroupMemberDto> _resourceAssembler;

    public GroupMemberController(
        ICommandHandler<AddGroupMemberCommand, BaseCommandResponse<Guid>> addMember,
        ICommandHandler<UpdateGroupMemberRoleCommand, BaseCommandResponse<Guid>> updateRole,
        ICommandHandler<DeleteGroupMemberCommand, BaseCommandResponse<Guid>> deleteMember,
        IQueryHandler<GetGroupMembersRequest, List<GroupMemberDto>> members,
        IQueryHandler<GetGroupMemberDetailsRequest, GroupMemberDto?> memberDetails,
        IResourceAssembler<GroupMemberDto, GroupMemberDto> resourceAssembler)
    {
        _addMember = addMember;
        _updateRole = updateRole;
        _deleteMember = deleteMember;
        _members = members;
        _memberDetails = memberDetails;
        _resourceAssembler = resourceAssembler;
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{groupId:guid}", Name = RouteNames.GetGroupMembers)]
    [OutputCache(PolicyName = "ListData")]
    [ProducesResponseType(typeof(HalCollectionResource<GroupMemberDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<HalCollectionResource<GroupMemberDto>>> GetByGroupId(Guid groupId, CancellationToken cancellationToken = default)
    {
        var members = await _members.QueryAsync(new GetGroupMembersRequest { GroupId = groupId }, cancellationToken);
        var halResource = await _resourceAssembler.ToCollectionResource(
            members,
            RouteNames.GetGroupMembers,
            new { groupId },
            HttpContext);

        return Ok(halResource);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("member/{id:guid}", Name = RouteNames.GetGroupMemberById)]
    [OutputCache(PolicyName = "DetailData")]
    [ProducesResponseType(typeof(HalResource<GroupMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<GroupMemberDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var member = await _memberDetails.QueryAsync(new GetGroupMemberDetailsRequest { Id = id }, cancellationToken);
        if (member is null)
        {
            return this.ToNotFoundProblem(GroupMemberNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(member, HttpContext);
        return Ok(halResource);
    }

    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateGroupMember)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Post([FromBody] AddGroupMemberDto dto, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId?.ToString();
        if (string.IsNullOrEmpty(userId))
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var command = new AddGroupMemberCommand
        {
            AddGroupMemberDto = dto,
            RequesterUserId = userId
        };

        var response = await _addMember.ExecuteAsync(command, cancellationToken);
        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, AddValidationProblem);
        }

        return Ok(response);
    }

    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPut("role", Name = RouteNames.UpdateGroupMember)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateRole([FromBody] UpdateGroupMemberRoleDto dto, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId?.ToString();
        if (string.IsNullOrEmpty(userId))
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var command = new UpdateGroupMemberRoleCommand
        {
            UpdateGroupMemberRoleDto = dto,
            RequesterUserId = userId
        };

        var response = await _updateRole.ExecuteAsync(command, cancellationToken);
        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteGroupMember)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId?.ToString();
        if (string.IsNullOrEmpty(userId))
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var command = new DeleteGroupMemberCommand
        {
            MemberId = id,
            RequesterUserId = userId
        };

        var response = await _deleteMember.ExecuteAsync(command, cancellationToken);
        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, DeleteValidationProblem);
        }

        return Ok(response);
    }

}
