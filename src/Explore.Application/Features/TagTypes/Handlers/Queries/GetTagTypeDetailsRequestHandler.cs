using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TagType;
using Explore.Application.Features.TagTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TagTypes.Handlers.Queries;

public class GetTagTypeDetailsRequestHandler : IRequestHandler<GetTagTypeDetailsRequest, TagTypeDto>
{
    private readonly ITagTypeRepository _repository;

    public GetTagTypeDetailsRequestHandler(ITagTypeRepository repository)
    {
        _repository = repository;
    }

    public async Task<TagTypeDto> Handle(GetTagTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var tagType = await _repository.GetTagTypeWithDetails(request.Id);
        return TagTypeMapper.ToDetail(tagType)!;
    }
}
