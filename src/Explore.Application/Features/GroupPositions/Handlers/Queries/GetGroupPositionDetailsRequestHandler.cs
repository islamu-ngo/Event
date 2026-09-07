using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.GroupPosition;
using Explore.Application.Features.GroupPositions.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.GroupPositions.Handlers.Queries;

public class GetGroupPositionDetailsRequestHandler : IRequestHandler<GetGroupPositionDetailsRequest, GroupPositionDto>
{
    private readonly IGroupPositionRepository _groupPositionRepository;
    private readonly IMapper _mapper;

    public GetGroupPositionDetailsRequestHandler(IGroupPositionRepository groupPositionRepository, IMapper mapper)
    {
        _groupPositionRepository = groupPositionRepository;
        _mapper = mapper;
    }

    public async Task<GroupPositionDto> Handle(GetGroupPositionDetailsRequest request, CancellationToken cancellationToken)
    {
        var groupPosition = await _groupPositionRepository.GetById(request.Id);
        return _mapper.Map<GroupPositionDto>(groupPosition);
    }
}
