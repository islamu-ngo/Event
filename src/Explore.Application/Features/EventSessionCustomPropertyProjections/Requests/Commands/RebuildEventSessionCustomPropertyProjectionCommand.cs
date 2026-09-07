using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Commands;

[AuthorizeResource(ResourceKinds.CustomPropertyProjection, AuthorizationActions.Update)]
public sealed record RebuildEventSessionCustomPropertyProjectionCommand : IRequest<BaseCommandResponse<RebuildProjectionResponseDto>>, ISecureRequest
{
    public required RebuildProjectionRequestDto RequestDto { get; init; }

    private Guid TenantId => RequestDto?.TenantId ?? Guid.Empty;

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new CustomPropertyProjectionAuthorizationFacts(TenantId, null, null);
}
