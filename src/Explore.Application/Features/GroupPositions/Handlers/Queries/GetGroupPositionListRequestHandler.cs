using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.GroupPosition;
using Explore.Application.Features.GroupPositions.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.GroupPositions.Handlers.Queries;

public class GetGroupPositionListRequestHandler : IQueryHandler<GetGroupPositionListRequest, List<GroupPositionListDto>>
{
    private readonly IGroupPositionRepository _groupPositionRepository;

    public GetGroupPositionListRequestHandler(IGroupPositionRepository groupPositionRepository)
    {
        _groupPositionRepository = groupPositionRepository;
    }

    public async Task<List<GroupPositionListDto>> QueryAsync(GetGroupPositionListRequest request, CancellationToken cancellationToken)
    {
        var groupPositions = await _groupPositionRepository.GetAll();
        return groupPositions.Select(GroupPositionMapper.ToListItem).ToList();
    }
}
