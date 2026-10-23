using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.GroupMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.GroupMembers.Handlers.Commands;

public class UpdateGroupMemberRoleCommandHandler : ICommandHandler<UpdateGroupMemberRoleCommand, BaseCommandResponse<Guid>>
{
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IUserContext _userContext;
    private readonly ITenantContext _tenantContext;
    private readonly IGroupMemberMutationBoundary _boundary;

    public UpdateGroupMemberRoleCommandHandler(
        IGroupMemberRepository groupMemberRepository,
        IUserContext userContext,
        ITenantContext tenantContext,
        IGroupMemberMutationBoundary boundary)
    {
        _groupMemberRepository = groupMemberRepository;
        _userContext = userContext;
        _tenantContext = tenantContext;
        _boundary = boundary;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateGroupMemberRoleCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated || _userContext.UserId is not Guid requesterGuid || requesterGuid == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Invalid requester User ID."],
                "Invalid requester User ID.");
        }

        return await _boundary.ExecuteRoleChangeAsync(request.UpdateGroupMemberRoleDto.Id,
            (int)request.UpdateGroupMemberRoleDto.Role, (actor, token) => UpdateAsync(request, actor, token), cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> UpdateAsync(UpdateGroupMemberRoleCommand request,
        Guid requesterGuid, CancellationToken cancellationToken)
    {
        var dto = request.UpdateGroupMemberRoleDto;

        var memberToUpdate = await _groupMemberRepository.GetGroupMemberWithDetails(dto.Id);
        if (memberToUpdate == null || memberToUpdate.TenantId != _tenantContext.TenantId)
        {
            return BaseCommandResponse.Validation<Guid>(["Member not found"], "Member not found");
        }

        var hasPermission = await _groupMemberRepository.HasPermissionInGroup(
            memberToUpdate.GroupTenant.GroupId, requesterGuid, PermissionCodes.GroupMemberUpdate);

        if (!hasPermission)
        {
            // Transitional fallback: allow GroupAdmin role
            var requesterMember = await _groupMemberRepository.GetByGroupAndUser(memberToUpdate.GroupTenant.GroupId, requesterGuid);
            if (requesterMember == null ||
                requesterMember.RoleId != (int)RoleEnum.GroupAdmin)
            {
                return BaseCommandResponse.Validation<Guid>(
                    ["You do not have permission to update roles."],
                    "You do not have permission to update roles.");
            }
        }

        // Prevent demoting the last admin
        var members = await _groupMemberRepository.GetMembersByGroupId(memberToUpdate.GroupTenant.GroupId);
        var adminCount = members.Count(m =>
            m.RoleId == (int)RoleEnum.GroupAdmin);

        if (memberToUpdate.RoleId == (int)RoleEnum.GroupAdmin &&
            (int)dto.Role != (int)RoleEnum.GroupAdmin &&
            adminCount <= 1)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Cannot demote the last admin of the group."],
                "Cannot demote the last admin of the group.");
        }

        if (memberToUpdate.RoleId == (int)dto.Role)
            return BaseCommandResponse.Success(memberToUpdate.Id, "Member role updated successfully");

        memberToUpdate.RoleId = (int)dto.Role;
        await _groupMemberRepository.Update(memberToUpdate);

        return BaseCommandResponse.Success(memberToUpdate.Id, "Member role updated successfully");
    }
}
