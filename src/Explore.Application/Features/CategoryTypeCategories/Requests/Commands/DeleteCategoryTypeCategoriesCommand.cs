using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Commands;

public sealed record DeleteCategoryTypeCategoriesCommand(Guid Id = default) : IRequest<bool>;
