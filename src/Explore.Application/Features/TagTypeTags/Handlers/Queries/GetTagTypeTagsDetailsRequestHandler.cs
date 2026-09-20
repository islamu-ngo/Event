using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagTypeTags;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypeTags.Handlers.Queries;

public class GetTagTypeTagsDetailsRequestHandler : IQueryHandler<GetTagTypeTagsDetailsRequest, TagTypeTagsDto?>
{
    private readonly ITagTypeTagsRepository _repository;

    public GetTagTypeTagsDetailsRequestHandler(ITagTypeTagsRepository repository)
    {
        _repository = repository;
    }

    public async Task<TagTypeTagsDto?> QueryAsync(GetTagTypeTagsDetailsRequest request, CancellationToken cancellationToken)
    {
        var tagTypeTags = await _repository.GetById(request.Id);
        return TagTypeTagsMapper.ToDetail(tagTypeTags);
    }
}
