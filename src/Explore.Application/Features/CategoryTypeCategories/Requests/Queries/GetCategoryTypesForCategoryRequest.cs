using Explore.Application.DTOs.CategoryType;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Queries;

public sealed record GetCategoryTypesForCategoryRequest(Guid CategoryId = default) : IRequest<List<CategoryTypeListDto>>;
