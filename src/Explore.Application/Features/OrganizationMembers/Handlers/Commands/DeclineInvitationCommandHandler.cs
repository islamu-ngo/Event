using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Commands;

public class DeclineInvitationCommandHandler : ICommandHandler<DeclineInvitationCommand, BaseCommandResponse<Guid>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IUserContext _userContext;
    private readonly IOrganizationMemberMutationBoundary _mutation;

    public DeclineInvitationCommandHandler(IOrganizationMemberRepository organizationMemberRepository,
        IUserContext userContext, IOrganizationMemberMutationBoundary mutation)
    {
        _organizationMemberRepository = organizationMemberRepository;
        _userContext = userContext;
        _mutation = mutation;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(DeclineInvitationCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated || _userContext.UserId is not Guid actor || actor == Guid.Empty)
            return BaseCommandResponse.Validation<Guid>(["Invalid requester User ID."], "Invalid requester User ID.");
        return await _mutation.ExecuteRemovalAsync(request.InvitationId, true,
            (recipient, token) => DeclineAsync(request, recipient, token), cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> DeclineAsync(DeclineInvitationCommand request, Guid recipient,
        CancellationToken cancellationToken)
    {
        var invitation = await _organizationMemberRepository.GetById(request.InvitationId);

        if (invitation == null)
        {
            return BaseCommandResponse.Validation<Guid>(["Invitation not found"], "Invitation not found");
        }

        if (invitation.UserId == Guid.Empty || invitation.UserId != recipient)
        {
            return BaseCommandResponse.Validation<Guid>(["Invitation not found"], "Invitation not found");
        }

        await _organizationMemberRepository.Delete(invitation);

        return BaseCommandResponse.Success(invitation.Id, "Invitation declined");
    }
}
