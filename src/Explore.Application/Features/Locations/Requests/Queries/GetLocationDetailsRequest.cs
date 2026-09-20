using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Location;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Locations.Requests.Queries;

[AuthorizeResource(ResourceKinds.Location, AuthorizationActions.Locations.View)]
public sealed record GetLocationDetailsRequest : IQuery<LocationDto?>, ISecureRequest
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => Id == Guid.Empty ? null : Id.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
