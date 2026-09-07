using System;
using Explore.Application.Authorization;
using MediatR;

namespace Explore.Application.Features.Categories.Requests.Commands;

[AuthorizeResource(ResourceKinds.Category, AuthorizationActions.Delete)]
public sealed record DeleteCategoryCommand : IRequest<bool>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
