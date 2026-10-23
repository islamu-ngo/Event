using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationMembers.Handlers.Queries;

public sealed class GetOrganizationMemberDetailsRequestHandler : IQueryHandler<GetOrganizationMemberDetailsRequest, OrganizationMemberDto?>
{
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IHumanAccountAdministrativeActionPreparation _preparation;
    private readonly ISerializableAttemptOwner _owner;

    public GetOrganizationMemberDetailsRequestHandler(
        IOrganizationMemberRepository organizationMemberRepository,
        IHumanAccountAdministrativeActionPreparation preparation, ISerializableAttemptOwner owner)
    {
        _organizationMemberRepository = organizationMemberRepository;
        _preparation = preparation;
        _owner = owner;
    }

    public async Task<OrganizationMemberDto?> QueryAsync(GetOrganizationMemberDetailsRequest request, CancellationToken cancellationToken)
    {
        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        return await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, token);
            var member = await _organizationMemberRepository.GetOrganizationMemberWithDetails(request.Id, token);
            return member is null ? null : OrganizationMapper.ToOrganizationMember(member);
        }, cancellationToken);
    }
}
