using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Category;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Categories.Requests.Commands;

[AuthorizeResource(ResourceKinds.Category, AuthorizationActions.Create)]
public sealed record CreateCategoryCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateCategoryDto CategoryDto { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => null;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
