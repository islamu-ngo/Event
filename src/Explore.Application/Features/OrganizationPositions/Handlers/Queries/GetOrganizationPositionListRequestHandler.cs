using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationPosition;
using Explore.Application.Features.OrganizationPositions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.OrganizationPositions.Handlers.Queries;

public class GetOrganizationPositionListRequestHandler : IRequestHandler<GetOrganizationPositionListRequest, List<OrganizationPositionListDto>>
{
    private readonly IOrganizationPositionRepository _organizationPositionRepository;

    public GetOrganizationPositionListRequestHandler(IOrganizationPositionRepository organizationPositionRepository)
    {
        _organizationPositionRepository = organizationPositionRepository;
    }

    public async Task<List<OrganizationPositionListDto>> Handle(GetOrganizationPositionListRequest request, CancellationToken cancellationToken)
    {
        var organizationPositions = await _organizationPositionRepository.GetAll();
        return organizationPositions.Select(OrganizationPositionMapper.ToListItem).ToList();
    }
}
