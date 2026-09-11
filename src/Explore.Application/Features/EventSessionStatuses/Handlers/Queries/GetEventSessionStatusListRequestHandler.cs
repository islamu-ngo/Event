using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionStatus;
using Explore.Application.Features.EventSessionStatuses.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionStatuses.Handlers.Queries;

public class GetEventSessionStatusListRequestHandler
    : IRequestHandler<GetEventSessionStatusListRequest, List<EventSessionStatusListDto>>
{
    private readonly IEventSessionStatusRepository _eventSessionStatusRepository;

    public GetEventSessionStatusListRequestHandler(
        IEventSessionStatusRepository eventSessionStatusRepository)
    {
        _eventSessionStatusRepository = eventSessionStatusRepository;
    }

    public async Task<List<EventSessionStatusListDto>> Handle(
        GetEventSessionStatusListRequest request,
        CancellationToken cancellationToken)
    {
        var statuses = await _eventSessionStatusRepository.GetAll();
        return statuses.Select(EventSessionStatusMapper.ToListItem).ToList();
    }
}
