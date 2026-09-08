using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagType;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Handlers.Queries;

public class GetTagTypesForTagRequestHandler : IRequestHandler<GetTagTypesForTagRequest, List<TagTypeListDto>>
{
    private readonly ITagTypeTagsRepository _repository;
    private readonly IMapper _mapper;

    public GetTagTypesForTagRequestHandler(ITagTypeTagsRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<TagTypeListDto>> Handle(GetTagTypesForTagRequest request, CancellationToken cancellationToken)
    {
        var tagTypes = await _repository.GetTagTypesForTag(request.TagId);
        return _mapper.Map<List<TagTypeListDto>>(tagTypes);
    }
}
