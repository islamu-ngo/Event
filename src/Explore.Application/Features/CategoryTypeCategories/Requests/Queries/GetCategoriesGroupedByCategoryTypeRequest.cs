using Explore.Application.DTOs.CategoryType;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Queries;

public sealed record GetCategoriesGroupedByCategoryTypeRequest : IRequest<List<CategoryTypeWithCategoriesDto>>
{
}
