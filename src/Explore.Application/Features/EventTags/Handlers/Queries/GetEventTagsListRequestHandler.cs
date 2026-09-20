using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTags;
using Explore.Application.Features.EventTags.Requests.Queries;
using Explore.Application.Contracts.Operations;
using EventTagsEntity = Explore.Domain.EventTags;

namespace Explore.Application.Features.EventTags.Handlers.Queries;

public class GetEventTagsListRequestHandler : IQueryHandler<GetEventTagsListRequest, List<EventTagsListDto>>
{
    private readonly IEventTagsRepository _eventTagsRepository;

    public GetEventTagsListRequestHandler(IEventTagsRepository eventTagsRepository)
    {
        _eventTagsRepository = eventTagsRepository;
    }

    public async Task<List<EventTagsListDto>> QueryAsync(GetEventTagsListRequest request, CancellationToken cancellationToken)
    {
        var eventTags = await _eventTagsRepository.GetAll();
        return eventTags.Select(EventMapper.ToListItem).ToList();
    }
}
