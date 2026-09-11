using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Queries;

public class GetSpeakersBySessionRequestHandler : IRequestHandler<GetSpeakersBySessionRequest, List<EventSessionSpeakerListDto>>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;

    public GetSpeakersBySessionRequestHandler(
        IEventSessionSpeakerRepository speakerRepository)
    {
        _speakerRepository = speakerRepository;
    }

    public async Task<List<EventSessionSpeakerListDto>> Handle(GetSpeakersBySessionRequest request, CancellationToken cancellationToken)
    {
        var speakers = await _speakerRepository.GetBySession(request.EventSessionId, cancellationToken);
        return speakers.Select(EventSessionMapper.ToListItem).ToList();
    }
}
