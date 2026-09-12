using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Queries;

public sealed class GetOrganizationMemberDetailsRequestHandler : IRequestHandler<GetOrganizationMemberDetailsRequest, OrganizationMemberDto?>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;

    public GetOrganizationMemberDetailsRequestHandler(
        IOrganizationMemberRepository organizationMemberRepository)
    {
        _organizationMemberRepository = organizationMemberRepository;
    }

    public async Task<OrganizationMemberDto?> Handle(GetOrganizationMemberDetailsRequest request, CancellationToken cancellationToken)
    {
        var member = await _organizationMemberRepository.GetOrganizationMemberWithDetails(request.Id);
        return member is null ? null : OrganizationMapper.ToOrganizationMember(member);
    }
}
