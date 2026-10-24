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

public class DeleteGroupMemberCommandHandler : ICommandHandler<DeleteGroupMemberCommand, BaseCommandResponse<Guid>>
{
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IUserContext _userContext;
    private readonly ITenantContext _tenantContext;
    private readonly IGroupMemberMutationBoundary _boundary;
    private readonly IHumanAccountAdministrativeActionPreparation _preparation;

    public DeleteGroupMemberCommandHandler(
        IGroupMemberRepository groupMemberRepository,
        IUserContext userContext,
        ITenantContext tenantContext,
        IGroupMemberMutationBoundary boundary,
        IHumanAccountAdministrativeActionPreparation preparation)
    {
        _groupMemberRepository = groupMemberRepository;
        _userContext = userContext;
        _tenantContext = tenantContext;
        _boundary = boundary;
        _preparation = preparation;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(DeleteGroupMemberCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated || _userContext.UserId is not Guid requesterGuid || requesterGuid == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Invalid requester User ID."],
                "Invalid requester User ID.");
        }

        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        return await _boundary.ExecuteRemovalAsync(request.MemberId,
            async (actor, token) =>
            {
                await prepared.AcquireAsync(request, token);
                var result = await DeleteAsync(request, actor, token);
                if (result.IsSuccess) prepared.Complete(result.Id);
                return result;
            }, cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> DeleteAsync(DeleteGroupMemberCommand request,
        Guid requesterGuid, CancellationToken cancellationToken)
    {
        var memberToDelete = await _groupMemberRepository.GetGroupMemberWithDetails(request.MemberId);
        if (memberToDelete == null || memberToDelete.TenantId != _tenantContext.TenantId)
        {
            return BaseCommandResponse.Validation<Guid>(["Member not found"], "Member not found");
        }

        var hasPermission = await _groupMemberRepository.HasPermissionInGroup(
            memberToDelete.GroupTenant.GroupId, requesterGuid, PermissionCodes.GroupMemberDelete);

        if (!hasPermission)
        {
            // Transitional fallback: allow GroupAdmin role
            var requesterMember = await _groupMemberRepository.GetByGroupAndUser(memberToDelete.GroupTenant.GroupId, requesterGuid);
            if (requesterMember == null ||
                requesterMember.RoleId != (int)RoleEnum.GroupAdmin)
            {
                return BaseCommandResponse.Validation<Guid>(
                    ["You do not have permission to remove members."],
                    "You do not have permission to remove members.");
            }
        }

        // Prevent removing the last admin
        var members = await _groupMemberRepository.GetMembersByGroupId(memberToDelete.GroupTenant.GroupId);
        var adminCount = members.Count(m =>
            m.RoleId == (int)RoleEnum.GroupAdmin);

        if (memberToDelete.RoleId == (int)RoleEnum.GroupAdmin &&
            adminCount <= 1)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Cannot remove the last admin of the group."],
                "Cannot remove the last admin of the group.");
        }

        await _groupMemberRepository.Delete(memberToDelete);

        return BaseCommandResponse.Success(memberToDelete.Id, "Member removed successfully");
    }
}
