using Explore.Application.Authorization;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventCustomPropertyProjections.Requests.Commands;

[AuthorizeResource(ResourceKinds.CustomPropertyProjection, AuthorizationActions.Update)]
public sealed record DrainCustomPropertyProjectionDirtyScopesCommand : IRequest<BaseCommandResponse<DrainDirtyScopesResponseDto>>, ISecureRequest
{
    public required DrainDirtyScopesRequestDto RequestDto { get; init; }

    private Guid TenantId => RequestDto?.TenantId ?? Guid.Empty;
    private string ProjectionName => RequestDto?.ProjectionName ?? string.Empty;

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty
        ? null
        : string.IsNullOrWhiteSpace(ProjectionName)
            ? TenantId.ToString("D")
            : $"{TenantId:D}:{ProjectionName}";

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new CustomPropertyProjectionAuthorizationFacts(TenantId);
}
