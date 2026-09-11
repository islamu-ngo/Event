using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tag;
using Explore.Application.Features.Tags.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Tags.Handlers.Queries;

public class GetTagListRequestHandler : IRequestHandler<GetTagListRequest, PaginatedResult<TagListDto>>
{
    private readonly ITagRepository _tagRepository;

    public GetTagListRequestHandler(
        ITagRepository tagRepository)
    {
        _tagRepository = tagRepository;
    }

    public async Task<PaginatedResult<TagListDto>> Handle(GetTagListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<TagListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var (tags, totalCount) = await _tagRepository.GetTagsWithDetailsPaged(pageNumber, pageSize);
        var dtos = tags.Select(TagMapper.ToListItem).ToList();
        return PaginatedResult<TagListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
