using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Commands;

public class UpdateOrganizationMemberRoleCommandHandler : ICommandHandler<UpdateOrganizationMemberRoleCommand, BaseCommandResponse<Guid>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserContext _userContext;
    private readonly IOrganizationMemberMutationBoundary _mutation;

    public UpdateOrganizationMemberRoleCommandHandler(
        IOrganizationMemberRepository organizationMemberRepository,
        IOrganizationRepository organizationRepository,
        IUserContext userContext,
        IOrganizationMemberMutationBoundary mutation)
    {
        _organizationMemberRepository = organizationMemberRepository;
        _organizationRepository = organizationRepository;
        _userContext = userContext;
        _mutation = mutation;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateOrganizationMemberRoleCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated || _userContext.UserId is not Guid requesterGuid || requesterGuid == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(["Invalid requester User ID."], "Invalid requester User ID.");
        }

        return await _mutation.ExecuteRoleChangeAsync(request.UpdateOrganizationMemberRoleDto.Id,
            (int)request.UpdateOrganizationMemberRoleDto.Role,
            (actor, token) => UpdateAsync(request, actor, token), cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> UpdateAsync(UpdateOrganizationMemberRoleCommand request,
        Guid requesterGuid, CancellationToken cancellationToken)
    {
        var dto = request.UpdateOrganizationMemberRoleDto;

        var memberToUpdate = await _organizationMemberRepository.GetOrganizationMemberWithDetails(dto.Id);
        if (memberToUpdate == null)
        {
            return BaseCommandResponse.Validation<Guid>(["Member not found"], "Member not found");
        }

        var organization = await _organizationRepository.GetById(memberToUpdate.OrganizationTenant.OrganizationId);
        if (organization == null)
        {
            return BaseCommandResponse.Validation<Guid>(["Organization not found"], "Organization not found");
        }

        // Check permissions - requester must be an Admin
        var members = await _organizationMemberRepository.GetMembersByOrganizationId(memberToUpdate.OrganizationTenant.OrganizationId);
        var requesterMember = members.FirstOrDefault(m => m.UserId == requesterGuid);
        if (requesterMember == null || requesterMember.RoleId != (int)RoleEnum.OrgAdmin)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["You do not have permission to update roles."],
                "You do not have permission to update roles.");
        }

        // Prevent demoting the last admin
        var adminCount = members.Count(m => m.RoleId == (int)RoleEnum.OrgAdmin);
        if (memberToUpdate.RoleId == (int)RoleEnum.OrgAdmin &&
            (int)dto.Role != (int)RoleEnum.OrgAdmin &&
            adminCount <= 1)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Cannot demote the last admin of the organization."],
                "Cannot demote the last admin of the organization.");
        }

        if (memberToUpdate.RoleId == (int)dto.Role)
            return BaseCommandResponse.Success(memberToUpdate.Id, "Member role unchanged");
        memberToUpdate.RoleId = (int)dto.Role;
        await _organizationMemberRepository.Update(memberToUpdate);

        return BaseCommandResponse.Success(memberToUpdate.Id, "Member role updated successfully");
    }
}
