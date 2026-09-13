using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Queries;

public class GetSpeakersBySessionQueryHandler : IQueryHandler<GetSpeakersBySessionQuery, List<EventSessionSpeakerListDto>>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;

    public GetSpeakersBySessionQueryHandler(
        IEventSessionSpeakerRepository speakerRepository)
    {
        _speakerRepository = speakerRepository;
    }

    public async Task<List<EventSessionSpeakerListDto>> QueryAsync(GetSpeakersBySessionQuery query, CancellationToken cancellationToken)
    {
        var speakers = await _speakerRepository.GetBySession(query.EventSessionId, cancellationToken);
        return speakers.Select(EventSessionMapper.ToListItem).ToList();
    }
}
