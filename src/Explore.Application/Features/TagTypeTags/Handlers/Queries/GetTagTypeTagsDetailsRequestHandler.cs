using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagTypeTags;
using Explore.Application.Features.TagTypeTags.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Handlers.Queries;

public class GetTagTypeTagsDetailsRequestHandler : IRequestHandler<GetTagTypeTagsDetailsRequest, TagTypeTagsDto>
{
    private readonly ITagTypeTagsRepository _repository;
    private readonly IMapper _mapper;

    public GetTagTypeTagsDetailsRequestHandler(ITagTypeTagsRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<TagTypeTagsDto> Handle(GetTagTypeTagsDetailsRequest request, CancellationToken cancellationToken)
    {
        var tagTypeTags = await _repository.GetById(request.Id);
        return _mapper.Map<TagTypeTagsDto>(tagTypeTags);
    }
}
