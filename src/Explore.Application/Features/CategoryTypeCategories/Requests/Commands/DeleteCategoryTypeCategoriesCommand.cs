using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Commands;

public sealed record DeleteCategoryTypeCategoriesCommand(Guid Id = default) : ICommand<bool>;
