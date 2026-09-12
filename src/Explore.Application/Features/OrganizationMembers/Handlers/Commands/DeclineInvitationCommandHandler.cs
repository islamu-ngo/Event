using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Commands;

public class DeclineInvitationCommandHandler : ICommandHandler<DeclineInvitationCommand, BaseCommandResponse<Guid>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;

    public DeclineInvitationCommandHandler(IOrganizationMemberRepository organizationMemberRepository)
    {
        _organizationMemberRepository = organizationMemberRepository;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(DeclineInvitationCommand request, CancellationToken cancellationToken)
    {
        var invitation = await _organizationMemberRepository.GetById(request.InvitationId);

        if (invitation == null)
        {
            return BaseCommandResponse.Validation<Guid>(["Invitation not found"], "Invitation not found");
        }

        if (invitation.UserId == Guid.Empty || invitation.UserId != request.UserId)
        {
            return BaseCommandResponse.Validation<Guid>(["Invitation not found"], "Invitation not found");
        }

        await _organizationMemberRepository.Delete(invitation);

        return BaseCommandResponse.Success(invitation.Id, "Invitation declined");
    }
}
