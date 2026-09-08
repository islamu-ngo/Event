using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventFormat;
using Explore.Application.Features.EventFormats.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventFormats.Handlers.Queries;

public class GetEventFormatDetailsRequestHandler : IRequestHandler<GetEventFormatDetailsRequest, EventFormatDto>
{
    private readonly IEventFormatRepository _eventFormatRepository;
    private readonly IMapper _mapper;

    public GetEventFormatDetailsRequestHandler(IEventFormatRepository eventFormatRepository, IMapper mapper)
    {
        _eventFormatRepository = eventFormatRepository;
        _mapper = mapper;
    }

    public async Task<EventFormatDto> Handle(GetEventFormatDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventFormat = await _eventFormatRepository.GetById(request.Id);
        return _mapper.Map<EventFormatDto>(eventFormat);
    }
}
