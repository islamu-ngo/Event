using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tag;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Handlers.Queries;

public class GetTagsByTagTypeRequestHandler : IRequestHandler<GetTagsByTagTypeRequest, List<TagListDto>>
{
    private readonly ITagTypeTagsRepository _repository;
    private readonly IMapper _mapper;

    public GetTagsByTagTypeRequestHandler(ITagTypeTagsRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<TagListDto>> Handle(GetTagsByTagTypeRequest request, CancellationToken cancellationToken)
    {
        var tags = await _repository.GetTagsByTagType(request.TagTypeId);
        return _mapper.Map<List<TagListDto>>(tags);
    }
}
