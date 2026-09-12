using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryType;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;

public class GetCategoryTypesForCategoryRequestHandler : IQueryHandler<GetCategoryTypesForCategoryRequest, List<CategoryTypeListDto>>
{
    private readonly ICategoryTypeCategoriesRepository _repository;

    public GetCategoryTypesForCategoryRequestHandler(ICategoryTypeCategoriesRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<CategoryTypeListDto>> QueryAsync(GetCategoryTypesForCategoryRequest request, CancellationToken cancellationToken)
    {
        var categoryTypes = await _repository.GetCategoryTypesForCategory(request.CategoryId);
        return categoryTypes.Select(CategoryTypeMapper.ToListItem).ToList();
    }
}
