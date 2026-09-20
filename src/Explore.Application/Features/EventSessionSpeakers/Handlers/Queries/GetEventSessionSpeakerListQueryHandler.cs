using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Queries;

public class GetEventSessionSpeakerListQueryHandler : IQueryHandler<GetEventSessionSpeakerListQuery, PaginatedResult<EventSessionSpeakerListDto>>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;

    public GetEventSessionSpeakerListQueryHandler(
        IEventSessionSpeakerRepository speakerRepository)
    {
        _speakerRepository = speakerRepository;
    }

    public async Task<PaginatedResult<EventSessionSpeakerListDto>> QueryAsync(GetEventSessionSpeakerListQuery query, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventSessionSpeakerListDto>.NormalizeParameters(query.PageNumber, query.PageSize);
        var (speakers, totalCount) = await _speakerRepository.GetSpeakersWithDetailsPaged(
            pageNumber,
            pageSize,
            cancellationToken);
        var dtos = speakers.Select(EventSessionMapper.ToListItem).ToList();
        return PaginatedResult<EventSessionSpeakerListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
