using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagTypeTags;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypeTags.Handlers.Queries;

public class GetTagTypeTagsListRequestHandler : IQueryHandler<GetTagTypeTagsListRequest, List<TagTypeTagsListDto>>
{
    private readonly ITagTypeTagsRepository _repository;

    public GetTagTypeTagsListRequestHandler(ITagTypeTagsRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<TagTypeTagsListDto>> QueryAsync(GetTagTypeTagsListRequest request, CancellationToken cancellationToken)
    {
        var tagTypeTags = await _repository.GetAll();
        return tagTypeTags.Select(TagTypeTagsMapper.ToListItem).ToList();
    }
}
