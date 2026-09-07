using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Category;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Categories.Requests.Commands;

[AuthorizeResource(ResourceKinds.Category, AuthorizationActions.Update)]
public sealed record UpdateCategoryCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid CategoryId { get; init; }

    public Guid ExpectedConcurrencyStamp { get; init; }

    public required UpdateCategoryDto UpdateCategoryDto { get; init; }

    string? ISecureRequest.ResourceId => CategoryId.ToString();
}
