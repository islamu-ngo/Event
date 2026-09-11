using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tag;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Handlers.Queries;

public class GetTagsByTagTypeRequestHandler : IRequestHandler<GetTagsByTagTypeRequest, List<TagListDto>>
{
    private readonly ITagTypeTagsRepository _repository;

    public GetTagsByTagTypeRequestHandler(ITagTypeTagsRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<TagListDto>> Handle(GetTagsByTagTypeRequest request, CancellationToken cancellationToken)
    {
        var tags = await _repository.GetTagsByTagType(request.TagTypeId);
        return tags.Select(TagMapper.ToListItem).ToList();
    }
}
