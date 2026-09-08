using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Commands;

public class UpdateOrganizationMemberRoleCommandHandler : IRequestHandler<UpdateOrganizationMemberRoleCommand, BaseCommandResponse<Guid>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IMapper _mapper;

    public UpdateOrganizationMemberRoleCommandHandler(
        IOrganizationMemberRepository organizationMemberRepository,
        IOrganizationRepository organizationRepository,
        IMapper mapper)
    {
        _organizationMemberRepository = organizationMemberRepository;
        _organizationRepository = organizationRepository;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateOrganizationMemberRoleCommand request, CancellationToken cancellationToken)
    {
        var dto = request.UpdateOrganizationMemberRoleDto;

        var memberToUpdate = await _organizationMemberRepository.GetById(dto.Id);
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
        if (Guid.TryParse(request.RequesterUserId, out Guid requesterGuid))
        {
            var requesterMember = members.FirstOrDefault(m => m.UserId == requesterGuid);
            // Only OrgAdmin role can update roles
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
        }
        else
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Invalid requester User ID."],
                "Invalid requester User ID.");
        }

        memberToUpdate.RoleId = (int)dto.Role;
        await _organizationMemberRepository.Update(memberToUpdate);

        return BaseCommandResponse.Success(memberToUpdate.Id, "Member role updated successfully");
    }
}
