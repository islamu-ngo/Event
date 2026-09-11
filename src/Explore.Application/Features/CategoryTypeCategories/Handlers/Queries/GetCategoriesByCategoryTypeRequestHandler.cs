using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Category;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;

public class GetCategoriesByCategoryTypeRequestHandler : IRequestHandler<GetCategoriesByCategoryTypeRequest, List<CategoryListDto>>
{
    private readonly ICategoryTypeCategoriesRepository _repository;

    public GetCategoriesByCategoryTypeRequestHandler(ICategoryTypeCategoriesRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<CategoryListDto>> Handle(GetCategoriesByCategoryTypeRequest request, CancellationToken cancellationToken)
    {
        var categories = await _repository.GetCategoriesByCategoryType(request.CategoryTypeId);
        return categories.Select(CustomPropertyMapper.ToListItem).ToList();
    }
}
