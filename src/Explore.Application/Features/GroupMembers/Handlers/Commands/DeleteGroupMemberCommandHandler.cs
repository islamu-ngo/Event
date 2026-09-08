using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.GroupMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.GroupMembers.Handlers.Commands;

public class DeleteGroupMemberCommandHandler : IRequestHandler<DeleteGroupMemberCommand, BaseCommandResponse<Guid>>
{
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IUserContext _userContext;
    private readonly ITenantContext _tenantContext;

    public DeleteGroupMemberCommandHandler(
        IGroupMemberRepository groupMemberRepository,
        IUserContext userContext,
        ITenantContext tenantContext)
    {
        _groupMemberRepository = groupMemberRepository;
        _userContext = userContext;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(DeleteGroupMemberCommand request, CancellationToken cancellationToken)
    {
        var memberToDelete = await _groupMemberRepository.GetById(request.MemberId);
        if (memberToDelete == null)
        {
            return BaseCommandResponse.Validation<Guid>(["Member not found"], "Member not found");
        }

        if (!Guid.TryParse(request.RequesterUserId, out Guid requesterGuid))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Invalid requester User ID."],
                "Invalid requester User ID.");
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
