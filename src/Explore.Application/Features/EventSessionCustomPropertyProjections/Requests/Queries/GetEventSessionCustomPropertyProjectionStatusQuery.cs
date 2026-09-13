using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Queries;

[AuthorizeResource(ResourceKinds.CustomPropertyProjection, AuthorizationActions.CustomPropertyProjections.View)]
public sealed record GetEventSessionCustomPropertyProjectionStatusQuery : IQuery<BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>>, ISecureRequest
{
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new CustomPropertyProjectionAuthorizationFacts(TenantId, null, null);
}
