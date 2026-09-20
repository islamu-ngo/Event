using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Category;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;

public class GetCategoriesByCategoryTypeRequestHandler : IQueryHandler<GetCategoriesByCategoryTypeRequest, List<CategoryListDto>>
{
    private readonly ICategoryTypeCategoriesRepository _repository;

    public GetCategoriesByCategoryTypeRequestHandler(ICategoryTypeCategoriesRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<CategoryListDto>> QueryAsync(GetCategoriesByCategoryTypeRequest request, CancellationToken cancellationToken)
    {
        var categories = await _repository.GetCategoriesByCategoryType(request.CategoryTypeId);
        return categories.Select(CustomPropertyMapper.ToListItem).ToList();
    }
}
