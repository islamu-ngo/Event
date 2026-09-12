using Explore.Application.DTOs.Category;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Queries;

public sealed record GetCategoriesByCategoryTypeRequest(int CategoryTypeId = default) : IQuery<List<CategoryListDto>>;
