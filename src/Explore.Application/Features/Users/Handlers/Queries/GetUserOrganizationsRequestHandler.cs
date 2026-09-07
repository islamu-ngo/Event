using AutoMapper;
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
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUserService;

    public GetUserOrganizationsRequestHandler(
        IOrganizationMemberRepository organizationMemberRepository,
        IMapper mapper,
        ICurrentUserService currentUserService)
    {
        _organizationMemberRepository = organizationMemberRepository;
        _mapper = mapper;
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
            var dto = _mapper.Map<OrganizationListDto>(membership.OrganizationTenant.Organization);
            dto.CurrentUserRoleId = membership.RoleId;
            dtos.Add(dto);
        }

        return dtos;
    }
}
