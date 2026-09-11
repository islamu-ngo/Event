using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryType;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;

public class GetCategoryTypesForCategoryRequestHandler : IRequestHandler<GetCategoryTypesForCategoryRequest, List<CategoryTypeListDto>>
{
    private readonly ICategoryTypeCategoriesRepository _repository;

    public GetCategoryTypesForCategoryRequestHandler(ICategoryTypeCategoriesRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<CategoryTypeListDto>> Handle(GetCategoryTypesForCategoryRequest request, CancellationToken cancellationToken)
    {
        var categoryTypes = await _repository.GetCategoryTypesForCategory(request.CategoryId);
        return categoryTypes.Select(CategoryTypeMapper.ToListItem).ToList();
    }
}
