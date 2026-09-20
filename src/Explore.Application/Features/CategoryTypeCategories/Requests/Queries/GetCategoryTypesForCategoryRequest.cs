using Explore.Application.DTOs.CategoryType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Queries;

public sealed record GetCategoryTypesForCategoryRequest(Guid CategoryId = default) : IQuery<List<CategoryTypeListDto>>;
