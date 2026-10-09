using Explore.Application.Authorization;
using Explore.Application.Models.Storage;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StorageObjects.Requests.Commands;

[AuthorizeResource(ResourceKinds.StorageObject, AuthorizationActions.Delete)]
public sealed record CancelStorageUploadSessionCommand : ICommand<StorageUploadSessionResult>, ISecureRequest
{
    public Guid UploadSessionId { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => UploadSessionId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new StorageObjectCollectionAuthorizationFacts(TenantId);
}
