using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tag;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypeTags.Handlers.Queries;

public class GetTagsByTagTypeRequestHandler : IQueryHandler<GetTagsByTagTypeRequest, List<TagListDto>>
{
    private readonly ITagTypeTagsRepository _repository;

    public GetTagsByTagTypeRequestHandler(ITagTypeTagsRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<TagListDto>> QueryAsync(GetTagsByTagTypeRequest request, CancellationToken cancellationToken)
    {
        var tags = await _repository.GetTagsByTagType(request.TagTypeId);
        return tags.Select(TagMapper.ToListItem).ToList();
    }
}
