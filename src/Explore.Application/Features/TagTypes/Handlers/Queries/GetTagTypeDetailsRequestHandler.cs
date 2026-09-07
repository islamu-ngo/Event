using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagType;
using Explore.Application.Features.TagTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TagTypes.Handlers.Queries;

public class GetTagTypeDetailsRequestHandler : IRequestHandler<GetTagTypeDetailsRequest, TagTypeDto>
{
    private readonly ITagTypeRepository _repository;
    private readonly IMapper _mapper;

    public GetTagTypeDetailsRequestHandler(ITagTypeRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<TagTypeDto> Handle(GetTagTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var tagType = await _repository.GetTagTypeWithDetails(request.Id);
        return _mapper.Map<TagTypeDto>(tagType);
    }
}
