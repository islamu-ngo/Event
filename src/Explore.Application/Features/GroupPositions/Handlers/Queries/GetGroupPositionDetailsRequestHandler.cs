using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.GroupPosition;
using Explore.Application.Features.GroupPositions.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.GroupPositions.Handlers.Queries;

public class GetGroupPositionDetailsRequestHandler : IQueryHandler<GetGroupPositionDetailsRequest, GroupPositionDto?>
{
    private readonly IGroupPositionRepository _groupPositionRepository;

    public GetGroupPositionDetailsRequestHandler(IGroupPositionRepository groupPositionRepository)
    {
        _groupPositionRepository = groupPositionRepository;
    }

    public async Task<GroupPositionDto?> QueryAsync(GetGroupPositionDetailsRequest request, CancellationToken cancellationToken)
    {
        var groupPosition = await _groupPositionRepository.GetById(request.Id);
        return GroupPositionMapper.ToDetail(groupPosition);
    }
}
