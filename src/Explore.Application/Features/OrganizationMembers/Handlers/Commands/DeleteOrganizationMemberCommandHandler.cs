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

public class DeleteOrganizationMemberCommandHandler : ICommandHandler<DeleteOrganizationMemberCommand, BaseCommandResponse<Guid>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserContext _userContext;
    private readonly IOrganizationMemberMutationBoundary _mutation;

    public DeleteOrganizationMemberCommandHandler(
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

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(DeleteOrganizationMemberCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated || _userContext.UserId is not Guid requesterGuid || requesterGuid == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(["Invalid requester User ID."], "Invalid requester User ID.");
        }

        return await _mutation.ExecuteRemovalAsync(request.MemberId, false,
            (actor, token) => DeleteAsync(request, actor, token), cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> DeleteAsync(DeleteOrganizationMemberCommand request,
        Guid requesterGuid, CancellationToken cancellationToken)
    {
        var memberToDelete = await _organizationMemberRepository.GetOrganizationMemberWithDetails(request.MemberId);

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
        var requesterMember = members.FirstOrDefault(m => m.UserId == requesterGuid);
        if (requesterMember == null || requesterMember.RoleId != (int)RoleEnum.OrgAdmin)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["You do not have permission to remove members."],
                "You do not have permission to remove members.");
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
