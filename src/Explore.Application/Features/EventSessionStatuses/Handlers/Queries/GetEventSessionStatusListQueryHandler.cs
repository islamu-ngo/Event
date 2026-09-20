using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionStatus;
using Explore.Application.Features.EventSessionStatuses.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionStatuses.Handlers.Queries;

public class GetEventSessionStatusListQueryHandler
    : IQueryHandler<GetEventSessionStatusListQuery, List<EventSessionStatusListDto>>
{
    private readonly IEventSessionStatusRepository _eventSessionStatusRepository;

    public GetEventSessionStatusListQueryHandler(
        IEventSessionStatusRepository eventSessionStatusRepository)
    {
        _eventSessionStatusRepository = eventSessionStatusRepository;
    }

    public async Task<List<EventSessionStatusListDto>> QueryAsync(
        GetEventSessionStatusListQuery query,
        CancellationToken cancellationToken)
    {
        var statuses = await _eventSessionStatusRepository.GetAll();
        return statuses.Select(EventSessionStatusMapper.ToListItem).ToList();
    }
}
