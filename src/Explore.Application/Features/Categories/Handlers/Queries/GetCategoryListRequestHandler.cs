using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Category;
using Explore.Application.Features.Categories.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Categories.Handlers.Queries;

public class GetCategoryListRequestHandler : IRequestHandler<GetCategoryListRequest, PaginatedResult<CategoryListDto>>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IMapper _mapper;
    private readonly HybridCache _cache;

    public GetCategoryListRequestHandler(
        ICategoryRepository categoryRepository,
        IMapper mapper,
        HybridCache cache)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
        _cache = cache;
    }

    public async Task<PaginatedResult<CategoryListDto>> Handle(GetCategoryListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<CategoryListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var cacheKey = $"categories:list:{pageNumber}:{pageSize}";

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var (categories, totalCount) = await _categoryRepository.GetCategoriesWithDetailsPaged(pageNumber, pageSize);
                var dtos = _mapper.Map<List<CategoryListDto>>(categories);
                return PaginatedResult<CategoryListDto>.Create(dtos, totalCount, pageNumber, pageSize);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(5),
                LocalCacheExpiration = TimeSpan.FromMinutes(1)
            },
            cancellationToken: cancellationToken);
    }
}
