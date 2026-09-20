using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventFormat;
using Explore.Application.Features.EventFormats.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventFormats.Handlers.Queries;

public class GetEventFormatDetailsRequestHandler : IQueryHandler<GetEventFormatDetailsRequest, EventFormatDto?>
{
    private readonly IEventFormatRepository _eventFormatRepository;

    public GetEventFormatDetailsRequestHandler(IEventFormatRepository eventFormatRepository)
    {
        _eventFormatRepository = eventFormatRepository;
    }

    public async Task<EventFormatDto?> QueryAsync(GetEventFormatDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventFormat = await _eventFormatRepository.GetById(request.Id);
        return EventFormatMapper.ToDetail(eventFormat);
    }
}
