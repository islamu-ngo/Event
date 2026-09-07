using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Location;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Locations.Requests.Commands;

[AuthorizeResource(ResourceKinds.Location, AuthorizationActions.Create)]
public sealed record CreateLocationCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateLocationDto LocationDto { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => null;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new TenantScopedAuthorizationFacts(TenantId);
}
