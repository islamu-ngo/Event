using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagTypeTags;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Handlers.Queries;

public class GetTagTypeTagsListRequestHandler : IRequestHandler<GetTagTypeTagsListRequest, List<TagTypeTagsListDto>>
{
    private readonly ITagTypeTagsRepository _repository;

    public GetTagTypeTagsListRequestHandler(ITagTypeTagsRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<TagTypeTagsListDto>> Handle(GetTagTypeTagsListRequest request, CancellationToken cancellationToken)
    {
        var tagTypeTags = await _repository.GetAll();
        return tagTypeTags.Select(TagTypeTagsMapper.ToListItem).ToList();
    }
}
