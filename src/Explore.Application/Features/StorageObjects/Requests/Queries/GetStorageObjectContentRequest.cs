using Explore.Application.Authorization;
using Explore.Application.Models.Storage;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StorageObjects.Requests.Queries;

[AuthorizeResource(ResourceKinds.StorageObject, AuthorizationActions.StorageObjects.Download)]
public sealed record GetStorageObjectContentRequest : IQuery<StorageObjectContentResult?>, ISecureRequest
{
    public Guid StorageObjectId { get; init; }

    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => StorageObjectId == Guid.Empty ? null : StorageObjectId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new StorageObjectCollectionAuthorizationFacts(TenantId);
}
