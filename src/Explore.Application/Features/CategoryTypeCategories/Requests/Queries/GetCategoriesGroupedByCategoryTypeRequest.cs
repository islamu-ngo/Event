using Explore.Application.DTOs.CategoryType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Queries;

public sealed record GetCategoriesGroupedByCategoryTypeRequest : IQuery<List<CategoryTypeWithCategoriesDto>>
{
}
