using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventFormat;
using Explore.Application.Features.EventFormats.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventFormats.Handlers.Queries;

public class GetEventFormatListRequestHandler : IRequestHandler<GetEventFormatListRequest, List<EventFormatListDto>>
{
    private readonly IEventFormatRepository _eventFormatRepository;
    private readonly IMapper _mapper;

    public GetEventFormatListRequestHandler(IEventFormatRepository eventFormatRepository, IMapper mapper)
    {
        _eventFormatRepository = eventFormatRepository;
        _mapper = mapper;
    }

    public async Task<List<EventFormatListDto>> Handle(GetEventFormatListRequest request, CancellationToken cancellationToken)
    {
        var eventFormats = await _eventFormatRepository.GetAll();
        return _mapper.Map<List<EventFormatListDto>>(eventFormats);
    }
}
