using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionStatus;
using Explore.Application.Features.EventSessionStatuses.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionStatuses.Handlers.Queries;

public class GetEventSessionStatusDetailsQueryHandler
    : IQueryHandler<GetEventSessionStatusDetailsQuery, EventSessionStatusDto?>
{
    private readonly IEventSessionStatusRepository _eventSessionStatusRepository;

    public GetEventSessionStatusDetailsQueryHandler(
        IEventSessionStatusRepository eventSessionStatusRepository)
    {
        _eventSessionStatusRepository = eventSessionStatusRepository;
    }

    public async Task<EventSessionStatusDto?> QueryAsync(
        GetEventSessionStatusDetailsQuery query,
        CancellationToken cancellationToken)
    {
        var status = await _eventSessionStatusRepository.GetById(query.Id);
        return EventSessionStatusMapper.ToDetail(status);
    }
}
