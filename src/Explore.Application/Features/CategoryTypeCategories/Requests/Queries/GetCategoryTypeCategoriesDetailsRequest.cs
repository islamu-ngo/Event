using Explore.Application.DTOs.CategoryTypeCategories;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Queries;

public sealed record GetCategoryTypeCategoriesDetailsRequest(Guid Id = default) : IRequest<CategoryTypeCategoriesDto>;
