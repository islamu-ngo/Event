using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;

public class GetCategoryTypeCategoriesDetailsRequestHandler : IQueryHandler<GetCategoryTypeCategoriesDetailsRequest, CategoryTypeCategoriesDto?>
{
    private readonly ICategoryTypeCategoriesRepository _repository;

    public GetCategoryTypeCategoriesDetailsRequestHandler(ICategoryTypeCategoriesRepository repository)
    {
        _repository = repository;
    }

    public async Task<CategoryTypeCategoriesDto?> QueryAsync(GetCategoryTypeCategoriesDetailsRequest request, CancellationToken cancellationToken)
    {
        var categoryTypeCategories = await _repository.GetById(request.Id);
        return CategoryTypeCategoriesMapper.ToDetail(categoryTypeCategories);
    }
}
