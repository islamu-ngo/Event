using Explore.Application.DTOs.CategoryTypeCategories;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Queries;

public sealed record GetCategoryTypeCategoriesListRequest : IRequest<List<CategoryTypeCategoriesListDto>>
{
}
