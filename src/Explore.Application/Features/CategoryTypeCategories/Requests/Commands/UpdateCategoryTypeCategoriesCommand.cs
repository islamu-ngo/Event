using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Requests.Commands;

public sealed record UpdateCategoryTypeCategoriesCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid CategoryTypeCategoriesId { get; init; }
    public required UpdateCategoryTypeCategoriesDto CategoryTypeCategoriesDto { get; init; }
}
