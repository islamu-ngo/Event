using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventFormat;
using Explore.Application.Features.EventFormats.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventFormats.Handlers.Queries;

public class GetEventFormatListRequestHandler : IQueryHandler<GetEventFormatListRequest, List<EventFormatListDto>>
{
    private readonly IEventFormatRepository _eventFormatRepository;

    public GetEventFormatListRequestHandler(IEventFormatRepository eventFormatRepository)
    {
        _eventFormatRepository = eventFormatRepository;
    }

    public async Task<List<EventFormatListDto>> QueryAsync(GetEventFormatListRequest request, CancellationToken cancellationToken)
    {
        var eventFormats = await _eventFormatRepository.GetAll();
        return eventFormats.Select(EventFormatMapper.ToListItem).ToList();
    }
}
