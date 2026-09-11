using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;

public class GetCategoryTypeCategoriesListRequestHandler : IRequestHandler<GetCategoryTypeCategoriesListRequest, List<CategoryTypeCategoriesListDto>>
{
    private readonly ICategoryTypeCategoriesRepository _repository;

    public GetCategoryTypeCategoriesListRequestHandler(ICategoryTypeCategoriesRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<CategoryTypeCategoriesListDto>> Handle(GetCategoryTypeCategoriesListRequest request, CancellationToken cancellationToken)
    {
        var categoryTypeCategories = await _repository.GetAll();
        return categoryTypeCategories.Select(CategoryTypeCategoriesMapper.ToListItem).ToList();
    }
}
