using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventStatus;
using Explore.Application.Features.EventStatuses.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventStatuses.Handlers.Queries;

public class GetEventStatusListRequestHandler : IRequestHandler<GetEventStatusListRequest, List<EventStatusListDto>>
{
    private readonly IEventStatusRepository _eventStatusRepository;

    public GetEventStatusListRequestHandler(IEventStatusRepository eventStatusRepository)
    {
        _eventStatusRepository = eventStatusRepository;
    }

    public async Task<List<EventStatusListDto>> Handle(GetEventStatusListRequest request, CancellationToken cancellationToken)
    {
        var eventStatuses = await _eventStatusRepository.GetAll();
        return eventStatuses.Select(EventStatusMapper.ToListItem).ToList();
    }
}
