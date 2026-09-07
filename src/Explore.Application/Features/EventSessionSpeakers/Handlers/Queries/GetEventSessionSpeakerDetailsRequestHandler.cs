using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Queries;

public class GetEventSessionSpeakerDetailsRequestHandler : IRequestHandler<GetEventSessionSpeakerDetailsRequest, EventSessionSpeakerDto>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;
    private readonly IMapper _mapper;

    public GetEventSessionSpeakerDetailsRequestHandler(
        IEventSessionSpeakerRepository speakerRepository,
        IMapper mapper)
    {
        _speakerRepository = speakerRepository;
        _mapper = mapper;
    }

    public async Task<EventSessionSpeakerDto> Handle(GetEventSessionSpeakerDetailsRequest request, CancellationToken cancellationToken)
    {
        var speaker = await _speakerRepository.GetById(request.Id);
        return _mapper.Map<EventSessionSpeakerDto>(speaker);
    }
}
