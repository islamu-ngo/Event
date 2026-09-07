using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.GroupPosition;
using Explore.Application.Features.GroupPositions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.GroupPositions.Handlers.Queries;

public class GetGroupPositionListRequestHandler : IRequestHandler<GetGroupPositionListRequest, List<GroupPositionListDto>>
{
    private readonly IGroupPositionRepository _groupPositionRepository;
    private readonly IMapper _mapper;

    public GetGroupPositionListRequestHandler(IGroupPositionRepository groupPositionRepository, IMapper mapper)
    {
        _groupPositionRepository = groupPositionRepository;
        _mapper = mapper;
    }

    public async Task<List<GroupPositionListDto>> Handle(GetGroupPositionListRequest request, CancellationToken cancellationToken)
    {
        var groupPositions = await _groupPositionRepository.GetAll();
        return _mapper.Map<List<GroupPositionListDto>>(groupPositions);
    }
}
