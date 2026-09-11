using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Queries;

public class GetSessionsByActorRequestHandler : IRequestHandler<GetSessionsByActorRequest, List<EventSessionSpeakerListDto>>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;

    public GetSessionsByActorRequestHandler(
        IEventSessionSpeakerRepository speakerRepository)
    {
        _speakerRepository = speakerRepository;
    }

    public async Task<List<EventSessionSpeakerListDto>> Handle(GetSessionsByActorRequest request, CancellationToken cancellationToken)
    {
        var speakers = await _speakerRepository.GetByActor(request.ActorId, cancellationToken);
        return speakers.Select(EventSessionMapper.ToListItem).ToList();
    }
}
