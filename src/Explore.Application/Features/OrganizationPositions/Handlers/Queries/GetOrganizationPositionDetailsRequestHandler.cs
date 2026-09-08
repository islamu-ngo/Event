using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationPosition;
using Explore.Application.Features.OrganizationPositions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.OrganizationPositions.Handlers.Queries;

public class GetOrganizationPositionDetailsRequestHandler : IRequestHandler<GetOrganizationPositionDetailsRequest, OrganizationPositionDto>
{
    private readonly IOrganizationPositionRepository _organizationPositionRepository;
    private readonly IMapper _mapper;

    public GetOrganizationPositionDetailsRequestHandler(IOrganizationPositionRepository organizationPositionRepository, IMapper mapper)
    {
        _organizationPositionRepository = organizationPositionRepository;
        _mapper = mapper;
    }

    public async Task<OrganizationPositionDto> Handle(GetOrganizationPositionDetailsRequest request, CancellationToken cancellationToken)
    {
        var organizationPosition = await _organizationPositionRepository.GetById(request.Id);
        return _mapper.Map<OrganizationPositionDto>(organizationPosition);
    }
}
