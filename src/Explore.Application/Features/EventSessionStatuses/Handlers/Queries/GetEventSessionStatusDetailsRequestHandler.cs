using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionStatus;
using Explore.Application.Features.EventSessionStatuses.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionStatuses.Handlers.Queries;

public class GetEventSessionStatusDetailsRequestHandler
    : IRequestHandler<GetEventSessionStatusDetailsRequest, EventSessionStatusDto>
{
    private readonly IEventSessionStatusRepository _eventSessionStatusRepository;
    private readonly IMapper _mapper;

    public GetEventSessionStatusDetailsRequestHandler(
        IEventSessionStatusRepository eventSessionStatusRepository,
        IMapper mapper)
    {
        _eventSessionStatusRepository = eventSessionStatusRepository;
        _mapper = mapper;
    }

    public async Task<EventSessionStatusDto> Handle(
        GetEventSessionStatusDetailsRequest request,
        CancellationToken cancellationToken)
    {
        var status = await _eventSessionStatusRepository.GetById(request.Id);
        return _mapper.Map<EventSessionStatusDto>(status);
    }
}
