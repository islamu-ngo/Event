using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTags;
using Explore.Application.Features.EventTags.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventTags.Handlers.Queries;

public class GetEventTagsDetailsRequestHandler : IQueryHandler<GetEventTagsDetailsRequest, EventTagsDto>
{
    private readonly IEventTagsRepository _eventTagsRepository;

    public GetEventTagsDetailsRequestHandler(IEventTagsRepository eventTagsRepository)
    {
        _eventTagsRepository = eventTagsRepository;
    }

    public async Task<EventTagsDto> QueryAsync(GetEventTagsDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventTags = await _eventTagsRepository.GetById(request.Id);
        // The existing request contract is non-nullable; a missing relationship still returns null.
        return EventMapper.ToDetail(eventTags)!;
    }
}
