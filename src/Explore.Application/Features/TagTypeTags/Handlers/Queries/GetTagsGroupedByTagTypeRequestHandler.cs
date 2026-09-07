// Queries the TagTypeTags junction table and groups results by TagType.

using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tag;
using Explore.Application.DTOs.TagType;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Handlers.Queries;

public class GetTagsGroupedByTagTypeRequestHandler
    : IRequestHandler<GetTagsGroupedByTagTypeRequest, List<TagTypeWithTagsDto>>
{
    private readonly ITagTypeTagsRepository _repository;
    private readonly IMapper _mapper;

    public GetTagsGroupedByTagTypeRequestHandler(ITagTypeTagsRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<TagTypeWithTagsDto>> Handle(
        GetTagsGroupedByTagTypeRequest request, CancellationToken cancellationToken)
    {
        var groups = await _repository.GetAllTagsGroupedByTagType();

        return groups.Select(g => new TagTypeWithTagsDto
        {
            Id = g.TagType.Id,
            FullName = g.TagType.FullName,
            Description = g.TagType.Description,
            Tags = _mapper.Map<List<TagListDto>>(g.Tags)
        }).ToList();
    }
}
