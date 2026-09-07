using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventStatus;
using Explore.Application.Features.EventStatuses.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventStatuses.Handlers.Queries;

public class GetEventStatusDetailsRequestHandler : IRequestHandler<GetEventStatusDetailsRequest, EventStatusDto>
{
    private readonly IEventStatusRepository _eventStatusRepository;
    private readonly IMapper _mapper;

    public GetEventStatusDetailsRequestHandler(IEventStatusRepository eventStatusRepository, IMapper mapper)
    {
        _eventStatusRepository = eventStatusRepository;
        _mapper = mapper;
    }

    public async Task<EventStatusDto> Handle(GetEventStatusDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventStatus = await _eventStatusRepository.GetById(request.Id);
        return _mapper.Map<EventStatusDto>(eventStatus);
    }
}
