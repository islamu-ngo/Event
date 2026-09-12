// Queries the TagTypeTags junction table and groups results by TagType.

using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagType;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypeTags.Handlers.Queries;

public class GetTagsGroupedByTagTypeRequestHandler
    : IQueryHandler<GetTagsGroupedByTagTypeRequest, List<TagTypeWithTagsDto>>
{
    private readonly ITagTypeTagsRepository _repository;

    public GetTagsGroupedByTagTypeRequestHandler(ITagTypeTagsRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<TagTypeWithTagsDto>> QueryAsync(
        GetTagsGroupedByTagTypeRequest request, CancellationToken cancellationToken)
    {
        var groups = await _repository.GetAllTagsGroupedByTagType();

        return groups.Select(g => new TagTypeWithTagsDto
        {
            Id = g.TagType.Id,
            FullName = g.TagType.FullName,
            Description = g.TagType.Description,
            Tags = g.Tags.Select(TagMapper.ToListItem).ToList()
        }).ToList();
    }
}
