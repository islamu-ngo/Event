using Explore.Application.Authorization;
using Explore.Application.DTOs.Geocoding;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Geocoding.Requests.Commands;

[AuthorizeResource(ResourceKinds.Location, AuthorizationActions.Locations.View)]
public sealed record CreateAddressSuggestionsCommand(
    Guid TenantId,
    AddressSuggestionsRequestDto Request)
    : ICommand<AddressSuggestionsResponseDto>, ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        TenantId == Guid.Empty ? null : TenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
            ? null
            : new TenantScopedAuthorizationFacts(TenantId);
}
