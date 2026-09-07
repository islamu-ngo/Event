using Explore.Application.Authorization;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Requests.Queries;

[AuthorizeResource(ResourceKinds.StorageObject, AuthorizationActions.StorageObjects.View)]
public sealed record GetStorageObjectListRequest : IRequest<PaginatedResult<StorageObjectListDto>>, ISecureRequest
{
    public Guid TenantId { get; init; }

    /// <summary>
    /// Gets or sets the page number (1-based). Defaults to 1.
    /// </summary>
    public int PageNumber { get; init; } = 1;

    /// <summary>
    /// Gets or sets the page size. Defaults to 20.
    /// </summary>
    public int PageSize { get; init; } = 20;

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new StorageObjectCollectionAuthorizationFacts(TenantId);
}
