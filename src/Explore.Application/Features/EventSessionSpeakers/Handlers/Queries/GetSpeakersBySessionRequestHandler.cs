using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Queries;

public class GetSpeakersBySessionRequestHandler : IRequestHandler<GetSpeakersBySessionRequest, List<EventSessionSpeakerListDto>>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;
    private readonly IMapper _mapper;

    public GetSpeakersBySessionRequestHandler(
        IEventSessionSpeakerRepository speakerRepository,
        IMapper mapper)
    {
        _speakerRepository = speakerRepository;
        _mapper = mapper;
    }

    public async Task<List<EventSessionSpeakerListDto>> Handle(GetSpeakersBySessionRequest request, CancellationToken cancellationToken)
    {
        var speakers = await _speakerRepository.GetBySession(request.EventSessionId, cancellationToken);
        return _mapper.Map<List<EventSessionSpeakerListDto>>(speakers);
    }
}
