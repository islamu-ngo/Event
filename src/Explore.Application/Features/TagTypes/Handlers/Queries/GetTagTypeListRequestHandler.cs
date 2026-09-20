using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagType;
using Explore.Application.Features.TagTypes.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypes.Handlers.Queries;

public class GetTagTypeListRequestHandler : IQueryHandler<GetTagTypeListRequest, List<TagTypeListDto>>
{
    private readonly ITagTypeRepository _repository;

    public GetTagTypeListRequestHandler(ITagTypeRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<TagTypeListDto>> QueryAsync(GetTagTypeListRequest request, CancellationToken cancellationToken)
    {
        var tagTypes = await _repository.GetTagTypesWithDetails();
        return tagTypes.Select(TagTypeMapper.ToListItem).ToList();
    }
}
