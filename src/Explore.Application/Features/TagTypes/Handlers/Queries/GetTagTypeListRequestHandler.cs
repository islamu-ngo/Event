using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagType;
using Explore.Application.Features.TagTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TagTypes.Handlers.Queries;

public class GetTagTypeListRequestHandler : IRequestHandler<GetTagTypeListRequest, List<TagTypeListDto>>
{
    private readonly ITagTypeRepository _repository;
    private readonly IMapper _mapper;

    public GetTagTypeListRequestHandler(ITagTypeRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<TagTypeListDto>> Handle(GetTagTypeListRequest request, CancellationToken cancellationToken)
    {
        var tagTypes = await _repository.GetTagTypesWithDetails();
        return _mapper.Map<List<TagTypeListDto>>(tagTypes);
    }
}
