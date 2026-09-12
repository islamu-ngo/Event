using Explore.Application.Mappings;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Organization;
using Explore.Application.Exceptions;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Users.Handlers.Queries;

/// <summary>
/// Handler to get all organizations a user is a member of.
/// Uses the OrganizationMember table to find memberships.
/// </summary>
public class GetUserOrganizationsRequestHandler : IRequestHandler<GetUserOrganizationsRequest, List<OrganizationListDto>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetUserOrganizationsRequestHandler(
        IOrganizationMemberRepository organizationMemberRepository,
        ICurrentUserService currentUserService)
    {
        _organizationMemberRepository = organizationMemberRepository;
        _currentUserService = currentUserService;
    }

    public async Task<List<OrganizationListDto>> Handle(GetUserOrganizationsRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.UserId
            ?? throw new AuthorizationException(ResourceKinds.OrganizationMember, AuthorizationActions.OrganizationMembers.View);

        if (request.UserId != currentUserId)
        {
            throw new AuthorizationException(ResourceKinds.OrganizationMember, AuthorizationActions.OrganizationMembers.View);
        }

        var memberships = await _organizationMemberRepository.GetMembershipsByUser(request.UserId, cancellationToken);

        var dtos = new List<OrganizationListDto>();

        foreach (var membership in memberships)
        {
            var dto = OrganizationMapper.ToOrganizationListItem(membership.OrganizationTenant.Organization);
            dto.CurrentUserRoleId = membership.RoleId;
            dtos.Add(dto);
        }

        return dtos;
    }
}
