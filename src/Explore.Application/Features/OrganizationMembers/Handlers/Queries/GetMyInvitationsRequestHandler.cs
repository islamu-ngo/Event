using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Identity;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Queries;

public class GetMyInvitationsRequestHandler : IQueryHandler<GetMyInvitationsRequest, List<OrganizationInvitationDto>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IUserContext _userContext;
    private readonly IAdminContext _adminContext;
    private readonly IMachinePrincipalAccessor _machinePrincipal;

    public GetMyInvitationsRequestHandler(IOrganizationMemberRepository organizationMemberRepository,
        IUserContext userContext, IAdminContext adminContext, IMachinePrincipalAccessor machinePrincipal)
    {
        _organizationMemberRepository = organizationMemberRepository;
        _userContext = userContext;
        _adminContext = adminContext;
        _machinePrincipal = machinePrincipal;
    }

    public async Task<List<OrganizationInvitationDto>> QueryAsync(GetMyInvitationsRequest request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated || _machinePrincipal.Current is not null) return [];
        var recipient = await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (recipient is null || recipient == Guid.Empty) return [];
        var invitations = await _organizationMemberRepository.GetInvitesByUserId(recipient.Value, cancellationToken);
        return invitations.Select(OrganizationMapper.ToOrganizationInvitation).ToList();
    }
}
