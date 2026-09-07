using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tag;
using Explore.Application.Features.Tags.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Tags.Handlers.Queries;

public class GetTagListRequestHandler : IRequestHandler<GetTagListRequest, PaginatedResult<TagListDto>>
{
    private readonly ITagRepository _tagRepository;
    private readonly IMapper _mapper;

    public GetTagListRequestHandler(
        ITagRepository tagRepository,
        IMapper mapper)
    {
        _tagRepository = tagRepository;
        _mapper = mapper;
    }

    public async Task<PaginatedResult<TagListDto>> Handle(GetTagListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<TagListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var (tags, totalCount) = await _tagRepository.GetTagsWithDetailsPaged(pageNumber, pageSize);
        var dtos = _mapper.Map<List<TagListDto>>(tags);
        return PaginatedResult<TagListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
