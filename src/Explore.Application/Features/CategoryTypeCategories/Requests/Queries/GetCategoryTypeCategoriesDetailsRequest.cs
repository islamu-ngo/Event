using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Queries;

public sealed record GetCategoryTypeCategoriesDetailsRequest(Guid Id = default) : IQuery<CategoryTypeCategoriesDto?>;
