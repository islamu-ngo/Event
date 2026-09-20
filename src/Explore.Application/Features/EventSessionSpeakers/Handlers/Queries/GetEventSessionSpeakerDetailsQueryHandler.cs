using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Queries;

public class GetEventSessionSpeakerDetailsQueryHandler : IQueryHandler<GetEventSessionSpeakerDetailsQuery, EventSessionSpeakerDto?>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;

    public GetEventSessionSpeakerDetailsQueryHandler(
        IEventSessionSpeakerRepository speakerRepository)
    {
        _speakerRepository = speakerRepository;
    }

    public async Task<EventSessionSpeakerDto?> QueryAsync(GetEventSessionSpeakerDetailsQuery query, CancellationToken cancellationToken)
    {
        var speaker = await _speakerRepository.GetWithDetails(query.Id, cancellationToken);
        return speaker is null ? null : EventSessionMapper.ToDetail(speaker);
    }
}
