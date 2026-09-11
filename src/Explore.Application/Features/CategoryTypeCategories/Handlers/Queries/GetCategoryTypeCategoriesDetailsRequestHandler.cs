using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;

public class GetCategoryTypeCategoriesDetailsRequestHandler : IRequestHandler<GetCategoryTypeCategoriesDetailsRequest, CategoryTypeCategoriesDto>
{
    private readonly ICategoryTypeCategoriesRepository _repository;

    public GetCategoryTypeCategoriesDetailsRequestHandler(ICategoryTypeCategoriesRepository repository)
    {
        _repository = repository;
    }

    public async Task<CategoryTypeCategoriesDto> Handle(GetCategoryTypeCategoriesDetailsRequest request, CancellationToken cancellationToken)
    {
        var categoryTypeCategories = await _repository.GetById(request.Id);
        return CategoryTypeCategoriesMapper.ToDetail(categoryTypeCategories)!;
    }
}
