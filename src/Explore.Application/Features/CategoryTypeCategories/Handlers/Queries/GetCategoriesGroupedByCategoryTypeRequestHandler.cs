using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Category;
using Explore.Application.DTOs.CategoryType;
using Explore.Application.Features.CategoryTypeCategories.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Queries;

public class GetCategoriesGroupedByCategoryTypeRequestHandler
    : IRequestHandler<GetCategoriesGroupedByCategoryTypeRequest, List<CategoryTypeWithCategoriesDto>>
{
    private readonly ICategoryTypeCategoriesRepository _repository;

    public GetCategoriesGroupedByCategoryTypeRequestHandler(ICategoryTypeCategoriesRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<CategoryTypeWithCategoriesDto>> Handle(
        GetCategoriesGroupedByCategoryTypeRequest request, CancellationToken cancellationToken)
    {
        var groups = await _repository.GetAllCategoriesGroupedByCategoryType();

        return groups.Select(g => new CategoryTypeWithCategoriesDto
        {
            Id = g.CategoryType.Id,
            FullName = g.CategoryType.FullName,
            Description = g.CategoryType.Description,
            Categories = g.Categories.Select(CustomPropertyMapper.ToListItem).ToList()
        }).ToList();
    }
}
