using Explore.Application.Authorization;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StorageObjects.Requests.Queries;

[AuthorizeResource(ResourceKinds.StorageObject, AuthorizationActions.StorageObjects.View)]
public sealed record GetStorageObjectDetailsRequest : IQuery<StorageObjectDto?>, ISecureRequest
{
    public Guid Id { get; init; }

    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => Id == Guid.Empty ? null : Id.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new StorageObjectCollectionAuthorizationFacts(TenantId);
}
