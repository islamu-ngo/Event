using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventFormat;
using Explore.Application.Features.EventFormats.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventFormats.Handlers.Queries;

public class GetEventFormatDetailsRequestHandler : IRequestHandler<GetEventFormatDetailsRequest, EventFormatDto>
{
    private readonly IEventFormatRepository _eventFormatRepository;

    public GetEventFormatDetailsRequestHandler(IEventFormatRepository eventFormatRepository)
    {
        _eventFormatRepository = eventFormatRepository;
    }

    public async Task<EventFormatDto> Handle(GetEventFormatDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventFormat = await _eventFormatRepository.GetById(request.Id);
        return EventFormatMapper.ToDetail(eventFormat)!;
    }
}
