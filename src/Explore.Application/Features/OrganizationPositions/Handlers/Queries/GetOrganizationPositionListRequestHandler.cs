using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationPosition;
using Explore.Application.Features.OrganizationPositions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.OrganizationPositions.Handlers.Queries;

public class GetOrganizationPositionListRequestHandler : IRequestHandler<GetOrganizationPositionListRequest, List<OrganizationPositionListDto>>
{
    private readonly IOrganizationPositionRepository _organizationPositionRepository;
    private readonly IMapper _mapper;

    public GetOrganizationPositionListRequestHandler(IOrganizationPositionRepository organizationPositionRepository, IMapper mapper)
    {
        _organizationPositionRepository = organizationPositionRepository;
        _mapper = mapper;
    }

    public async Task<List<OrganizationPositionListDto>> Handle(GetOrganizationPositionListRequest request, CancellationToken cancellationToken)
    {
        var organizationPositions = await _organizationPositionRepository.GetAll();
        return _mapper.Map<List<OrganizationPositionListDto>>(organizationPositions);
    }
}
