using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventStatus;
using Explore.Application.Features.EventStatuses.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventStatuses.Handlers.Queries;

public class GetEventStatusListRequestHandler : IRequestHandler<GetEventStatusListRequest, List<EventStatusListDto>>
{
    private readonly IEventStatusRepository _eventStatusRepository;
    private readonly IMapper _mapper;

    public GetEventStatusListRequestHandler(IEventStatusRepository eventStatusRepository, IMapper mapper)
    {
        _eventStatusRepository = eventStatusRepository;
        _mapper = mapper;
    }

    public async Task<List<EventStatusListDto>> Handle(GetEventStatusListRequest request, CancellationToken cancellationToken)
    {
        var eventStatuses = await _eventStatusRepository.GetAll();
        return _mapper.Map<List<EventStatusListDto>>(eventStatuses);
    }
}
