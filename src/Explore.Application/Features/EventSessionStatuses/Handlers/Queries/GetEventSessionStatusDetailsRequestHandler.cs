using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionStatus;
using Explore.Application.Features.EventSessionStatuses.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionStatuses.Handlers.Queries;

public class GetEventSessionStatusDetailsRequestHandler
    : IRequestHandler<GetEventSessionStatusDetailsRequest, EventSessionStatusDto>
{
    private readonly IEventSessionStatusRepository _eventSessionStatusRepository;

    public GetEventSessionStatusDetailsRequestHandler(
        IEventSessionStatusRepository eventSessionStatusRepository)
    {
        _eventSessionStatusRepository = eventSessionStatusRepository;
    }

    public async Task<EventSessionStatusDto> Handle(
        GetEventSessionStatusDetailsRequest request,
        CancellationToken cancellationToken)
    {
        var status = await _eventSessionStatusRepository.GetById(request.Id);
        return EventSessionStatusMapper.ToDetail(status)!;
    }
}
