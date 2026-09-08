using Explore.Application.Authorization;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventCustomPropertyProjections.Requests.Queries;

[AuthorizeResource(ResourceKinds.CustomPropertyProjection, AuthorizationActions.CustomPropertyProjections.View)]
public sealed record GetCustomPropertyProjectionDirtyScopesQuery : IRequest<PaginatedResult<ProjectionDirtyScopeDto>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public string ProjectionName { get; init; } = string.Empty;
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = PaginatedResult<ProjectionDirtyScopeDto>.DefaultPageSize;

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty
        ? null
        : string.IsNullOrWhiteSpace(ProjectionName)
            ? TenantId.ToString("D")
            : $"{TenantId:D}:{ProjectionName}";

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new CustomPropertyProjectionAuthorizationFacts(TenantId);
}
