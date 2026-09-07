using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Queries;

public class GetSessionsByActorRequestHandler : IRequestHandler<GetSessionsByActorRequest, List<EventSessionSpeakerListDto>>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;
    private readonly IMapper _mapper;

    public GetSessionsByActorRequestHandler(
        IEventSessionSpeakerRepository speakerRepository,
        IMapper mapper)
    {
        _speakerRepository = speakerRepository;
        _mapper = mapper;
    }

    public async Task<List<EventSessionSpeakerListDto>> Handle(GetSessionsByActorRequest request, CancellationToken cancellationToken)
    {
        var speakers = await _speakerRepository.GetByActor(request.ActorId, cancellationToken);
        return _mapper.Map<List<EventSessionSpeakerListDto>>(speakers);
    }
}
