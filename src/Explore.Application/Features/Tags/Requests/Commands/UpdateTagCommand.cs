using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Tag;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Tags.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tag, AuthorizationActions.Update)]
public sealed record UpdateTagCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TagId { get; init; }
    public Guid TenantId { get; init; }
    public required UpdateTagDto Update { get; init; }

    string? ISecureRequest.ResourceId => TagId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
