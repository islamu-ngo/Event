using Explore.Application.Authorization;
using Explore.Application.DTOs.Geocoding;
using MediatR;

namespace Explore.Application.Features.Geocoding.Requests.Queries;

[AuthorizeResource(ResourceKinds.Location, AuthorizationActions.Locations.View)]
public sealed record GetAddressSuggestionsQuery(
    Guid TenantId,
    AddressSuggestionsRequestDto Request)
    : IRequest<AddressSuggestionsResponseDto>, ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        TenantId == Guid.Empty ? null : TenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
            ? null
            : new TenantScopedAuthorizationFacts(TenantId);
}
