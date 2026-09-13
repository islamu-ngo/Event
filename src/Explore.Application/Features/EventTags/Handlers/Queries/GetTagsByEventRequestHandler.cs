using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tag;
using Explore.Application.Features.EventTags.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventTags.Handlers.Queries;

public class GetTagsByEventRequestHandler : IQueryHandler<GetTagsByEventRequest, List<TagListDto>>
{
    private readonly IEventTagsRepository _eventTagsRepository;

    public GetTagsByEventRequestHandler(IEventTagsRepository eventTagsRepository)
    {
        _eventTagsRepository = eventTagsRepository;
    }

    public async Task<List<TagListDto>> QueryAsync(GetTagsByEventRequest request, CancellationToken cancellationToken)
    {
        var tags = await _eventTagsRepository.GetTagsByEvent(request.EventId);
        return tags.Select(TagMapper.ToListItem).ToList();
    }
}
