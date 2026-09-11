using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationPosition;
using Explore.Application.Features.OrganizationPositions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.OrganizationPositions.Handlers.Queries;

public class GetOrganizationPositionDetailsRequestHandler : IRequestHandler<GetOrganizationPositionDetailsRequest, OrganizationPositionDto>
{
    private readonly IOrganizationPositionRepository _organizationPositionRepository;

    public GetOrganizationPositionDetailsRequestHandler(IOrganizationPositionRepository organizationPositionRepository)
    {
        _organizationPositionRepository = organizationPositionRepository;
    }

    public async Task<OrganizationPositionDto> Handle(GetOrganizationPositionDetailsRequest request, CancellationToken cancellationToken)
    {
        var organizationPosition = await _organizationPositionRepository.GetById(request.Id);
        return OrganizationPositionMapper.ToDetail(organizationPosition)!;
    }
}
