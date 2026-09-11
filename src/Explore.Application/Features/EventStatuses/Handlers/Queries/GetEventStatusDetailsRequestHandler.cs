using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventStatus;
using Explore.Application.Features.EventStatuses.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventStatuses.Handlers.Queries;

public class GetEventStatusDetailsRequestHandler : IRequestHandler<GetEventStatusDetailsRequest, EventStatusDto>
{
    private readonly IEventStatusRepository _eventStatusRepository;

    public GetEventStatusDetailsRequestHandler(IEventStatusRepository eventStatusRepository)
    {
        _eventStatusRepository = eventStatusRepository;
    }

    public async Task<EventStatusDto> Handle(GetEventStatusDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventStatus = await _eventStatusRepository.GetById(request.Id);
        return EventStatusMapper.ToDetail(eventStatus)!;
    }
}
