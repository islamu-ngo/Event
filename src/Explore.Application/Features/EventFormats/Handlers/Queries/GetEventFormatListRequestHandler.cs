using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventFormat;
using Explore.Application.Features.EventFormats.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventFormats.Handlers.Queries;

public class GetEventFormatListRequestHandler : IRequestHandler<GetEventFormatListRequest, List<EventFormatListDto>>
{
    private readonly IEventFormatRepository _eventFormatRepository;

    public GetEventFormatListRequestHandler(IEventFormatRepository eventFormatRepository)
    {
        _eventFormatRepository = eventFormatRepository;
    }

    public async Task<List<EventFormatListDto>> Handle(GetEventFormatListRequest request, CancellationToken cancellationToken)
    {
        var eventFormats = await _eventFormatRepository.GetAll();
        return eventFormats.Select(EventFormatMapper.ToListItem).ToList();
    }
}
