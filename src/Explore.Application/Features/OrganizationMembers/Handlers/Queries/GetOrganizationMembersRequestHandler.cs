using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Queries;

public class GetOrganizationMembersRequestHandler : IQueryHandler<GetOrganizationMembersRequest, List<OrganizationMemberDto>>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IHumanAccountAdministrativeActionPreparation _preparation;
    private readonly ISerializableAttemptOwner _owner;

    public GetOrganizationMembersRequestHandler(IOrganizationMemberRepository organizationMemberRepository,
        IHumanAccountAdministrativeActionPreparation preparation, ISerializableAttemptOwner owner)
    {
        _organizationMemberRepository = organizationMemberRepository;
        _preparation = preparation;
        _owner = owner;
    }

    public async Task<List<OrganizationMemberDto>> QueryAsync(GetOrganizationMembersRequest request, CancellationToken cancellationToken)
    {
        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        return await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, token);
            var members = await _organizationMemberRepository.GetMembersByOrganizationId(request.OrganizationId);
            return members.Select(OrganizationMapper.ToOrganizationMember).ToList();
        }, cancellationToken);
    }
}
