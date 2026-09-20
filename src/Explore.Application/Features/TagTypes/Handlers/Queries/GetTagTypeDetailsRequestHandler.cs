using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagType;
using Explore.Application.Features.TagTypes.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypes.Handlers.Queries;

public class GetTagTypeDetailsRequestHandler : IQueryHandler<GetTagTypeDetailsRequest, TagTypeDto?>
{
    private readonly ITagTypeRepository _repository;

    public GetTagTypeDetailsRequestHandler(ITagTypeRepository repository)
    {
        _repository = repository;
    }

    public async Task<TagTypeDto?> QueryAsync(GetTagTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var tagType = await _repository.GetTagTypeWithDetails(request.Id);
        return TagTypeMapper.ToDetail(tagType);
    }
}
