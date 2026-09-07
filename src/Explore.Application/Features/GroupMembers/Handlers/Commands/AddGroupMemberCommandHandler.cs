using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.GroupMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.GroupMembers.Handlers.Commands;

public class AddGroupMemberCommandHandler : IRequestHandler<AddGroupMemberCommand, BaseCommandResponse<Guid>>
{
    private readonly IGroupRepository _groupRepository;
    private readonly IGroupTenantRepository _groupTenantRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserContext _userContext;
    private readonly ITenantContext _tenantContext;

    public AddGroupMemberCommandHandler(
        IGroupRepository groupRepository,
        IGroupTenantRepository groupTenantRepository,
        IGroupMemberRepository groupMemberRepository,
        IUserRepository userRepository,
        IUserContext userContext,
        ITenantContext tenantContext)
    {
        _groupRepository = groupRepository;
        _groupTenantRepository = groupTenantRepository;
        _groupMemberRepository = groupMemberRepository;
        _userRepository = userRepository;
        _userContext = userContext;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(AddGroupMemberCommand request, CancellationToken cancellationToken)
    {
        var dto = request.AddGroupMemberDto;

        var group = await _groupRepository.GetById(dto.GroupId);
        if (group == null)
        {
            return BaseCommandResponse.Validation<Guid>(["Group not found"], "Group not found");
        }

        var participation = await _groupTenantRepository.GetByGroupAndTenant(
            dto.GroupId,
            _tenantContext.TenantId,
            cancellationToken);
        if (participation is null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Group does not participate in the current tenant."],
                "Group does not participate in the current tenant.");
        }

        if (!Guid.TryParse(request.RequesterUserId, out Guid requesterGuid))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Invalid requester User ID."],
                "Invalid requester User ID.");
        }

        var hasPermission = await _groupMemberRepository.HasPermissionInGroup(
            dto.GroupId, requesterGuid, PermissionCodes.GroupMemberCreate);

        if (!hasPermission)
        {
            // Transitional fallback: allow GroupAdmin role if no permissions are seeded
            var requesterMember = await _groupMemberRepository.GetByGroupAndUser(dto.GroupId, requesterGuid);
            if (requesterMember == null ||
                requesterMember.RoleId != (int)RoleEnum.GroupAdmin)
            {
                return BaseCommandResponse.Validation<Guid>(
                    ["You do not have permission to add members."],
                    "You do not have permission to add members.");
            }
        }

        var userToAdd = await _userRepository.GetUserByEmail(dto.Email);
        if (userToAdd == null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["User with this email not found."],
                "User with this email not found.");
        }

        var alreadyMember = await _groupMemberRepository.Exists(dto.GroupId, userToAdd.Id);
        if (alreadyMember)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["User is already a member of this group."],
                "User is already a member of this group.");
        }

        var groupMember = new GroupMember
        {
            GroupTenantId = participation.Id,
            GroupTenant = participation,
            UserId = userToAdd.Id,
            User = null!,
            RoleId = (int)dto.Role,
            Role = null!,
            GroupPositionId = dto.GroupPositionId,
            TenantId = _tenantContext.TenantId,
            Tenant = null!
        };

        groupMember = await _groupMemberRepository.Create(groupMember);

        return BaseCommandResponse.Success(groupMember.Id, "Member added successfully");
    }
}
