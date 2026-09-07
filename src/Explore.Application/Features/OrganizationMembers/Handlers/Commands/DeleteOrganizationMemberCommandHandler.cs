using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Commands;

public class DeleteOrganizationMemberCommandHandler : IRequestHandler<DeleteOrganizationMemberCommand, BaseCommandResponse<Guid>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IOrganizationRepository _organizationRepository;

    public DeleteOrganizationMemberCommandHandler(
        IOrganizationMemberRepository organizationMemberRepository,
        IOrganizationRepository organizationRepository)
    {
        _organizationMemberRepository = organizationMemberRepository;
        _organizationRepository = organizationRepository;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(DeleteOrganizationMemberCommand request, CancellationToken cancellationToken)
    {
        var memberToDelete = await _organizationMemberRepository.GetById(request.MemberId);

        if (memberToDelete == null)
        {
            return BaseCommandResponse.Validation<Guid>(["Member not found"], "Member not found");
        }

        var organization = await _organizationRepository.GetById(memberToDelete.OrganizationTenant.OrganizationId);
        if (organization == null)
        {
            return BaseCommandResponse.Validation<Guid>(["Organization not found"], "Organization not found");
        }

        // Check permissions - requester must be an Admin
        var members = await _organizationMemberRepository.GetMembersByOrganizationId(memberToDelete.OrganizationTenant.OrganizationId);
        if (Guid.TryParse(request.RequesterUserId, out Guid requesterGuid))
        {
            var requesterMember = members.FirstOrDefault(m => m.UserId == requesterGuid);
            // Only OrgAdmin role can remove members
            if (requesterMember == null || requesterMember.RoleId != (int)RoleEnum.OrgAdmin)
            {
                return BaseCommandResponse.Validation<Guid>(
                    ["You do not have permission to remove members."],
                    "You do not have permission to remove members.");
            }
        }
        else
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Invalid requester User ID."],
                "Invalid requester User ID.");
        }

        // Prevent self-deletion if they are the only Admin
        var adminCount = members.Count(m => m.RoleId == (int)RoleEnum.OrgAdmin);
        if (memberToDelete.RoleId == (int)RoleEnum.OrgAdmin && adminCount <= 1)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Cannot remove the last admin of the organization."],
                "Cannot remove the last admin of the organization.");
        }

        await _organizationMemberRepository.Delete(memberToDelete);

        return BaseCommandResponse.Success(memberToDelete.Id, "Member removed successfully");
    }
}
