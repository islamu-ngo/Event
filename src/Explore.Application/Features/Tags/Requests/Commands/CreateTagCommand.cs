using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Tag;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Tags.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tag, AuthorizationActions.Create)]
public sealed record CreateTagCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateTagDto TagDto { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => null;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
