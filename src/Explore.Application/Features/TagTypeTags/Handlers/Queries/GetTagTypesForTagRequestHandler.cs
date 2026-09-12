using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagType;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypeTags.Handlers.Queries;

public class GetTagTypesForTagRequestHandler : IQueryHandler<GetTagTypesForTagRequest, List<TagTypeListDto>>
{
    private readonly ITagTypeTagsRepository _repository;

    public GetTagTypesForTagRequestHandler(ITagTypeTagsRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<TagTypeListDto>> QueryAsync(GetTagTypesForTagRequest request, CancellationToken cancellationToken)
    {
        var tagTypes = await _repository.GetTagTypesForTag(request.TagId);
        return tagTypes.Select(TagTypeMapper.ToListItem).ToList();
    }
}
