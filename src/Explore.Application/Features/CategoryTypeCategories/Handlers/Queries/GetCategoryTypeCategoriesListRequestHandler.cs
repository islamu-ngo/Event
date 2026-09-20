using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;

public class GetCategoryTypeCategoriesListRequestHandler : IQueryHandler<GetCategoryTypeCategoriesListRequest, List<CategoryTypeCategoriesListDto>>
{
    private readonly ICategoryTypeCategoriesRepository _repository;

    public GetCategoryTypeCategoriesListRequestHandler(ICategoryTypeCategoriesRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<CategoryTypeCategoriesListDto>> QueryAsync(GetCategoryTypeCategoriesListRequest request, CancellationToken cancellationToken)
    {
        var categoryTypeCategories = await _repository.GetAll();
        return categoryTypeCategories.Select(CategoryTypeCategoriesMapper.ToListItem).ToList();
    }
}
