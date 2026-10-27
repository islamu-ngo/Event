using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Queries;

public class ValidateOrganizationInvitationQueryHandler : IQueryHandler<ValidateOrganizationInvitationQuery, BaseCommandResponse<Guid>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IUserContext _userContext;
    private readonly IAdminContext _adminContext;
    private readonly IMachinePrincipalAccessor _machinePrincipal;

    public ValidateOrganizationInvitationQueryHandler(IOrganizationMemberRepository organizationMemberRepository,
        IUserContext userContext, IAdminContext adminContext, IMachinePrincipalAccessor machinePrincipal)
    {
        _organizationMemberRepository = organizationMemberRepository;
        _userContext = userContext;
        _adminContext = adminContext;
        _machinePrincipal = machinePrincipal;
    }

    public async Task<BaseCommandResponse<Guid>> QueryAsync(ValidateOrganizationInvitationQuery request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated || _machinePrincipal.Current is not null)
            return BaseCommandResponse.Validation<Guid>(["Invitation not found"], "Invitation not found");

        var recipient = await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (recipient is null || recipient == Guid.Empty)
            return BaseCommandResponse.Validation<Guid>(["Invitation not found"], "Invitation not found");

        var invitation = await _organizationMemberRepository.GetOrganizationMemberWithDetails(request.InvitationId, cancellationToken);

        if (invitation == null)
        {
            return BaseCommandResponse.Validation<Guid>(["Invitation not found"], "Invitation not found");
        }

        if (invitation.UserId == Guid.Empty || invitation.UserId != recipient)
        {
            return BaseCommandResponse.Validation<Guid>(["Invitation not found"], "Invitation not found");
        }

        return BaseCommandResponse.Success(invitation.Id, "Invitation accepted");
    }
}
