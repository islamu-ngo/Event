using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagType;
using Explore.Application.Features.TagTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TagTypes.Handlers.Queries;

public class GetTagTypeListRequestHandler : IRequestHandler<GetTagTypeListRequest, List<TagTypeListDto>>
{
    private readonly ITagTypeRepository _repository;

    public GetTagTypeListRequestHandler(ITagTypeRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<TagTypeListDto>> Handle(GetTagTypeListRequest request, CancellationToken cancellationToken)
    {
        var tagTypes = await _repository.GetTagTypesWithDetails();
        return tagTypes.Select(TagTypeMapper.ToListItem).ToList();
    }
}
