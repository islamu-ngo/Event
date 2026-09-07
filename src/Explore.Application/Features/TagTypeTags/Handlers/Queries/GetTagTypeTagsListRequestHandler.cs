using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagTypeTags;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Handlers.Queries;

public class GetTagTypeTagsListRequestHandler : IRequestHandler<GetTagTypeTagsListRequest, List<TagTypeTagsListDto>>
{
    private readonly ITagTypeTagsRepository _repository;
    private readonly IMapper _mapper;

    public GetTagTypeTagsListRequestHandler(ITagTypeTagsRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<TagTypeTagsListDto>> Handle(GetTagTypeTagsListRequest request, CancellationToken cancellationToken)
    {
        var tagTypeTags = await _repository.GetAll();
        return _mapper.Map<List<TagTypeTagsListDto>>(tagTypeTags);
    }
}
