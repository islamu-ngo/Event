using Explore.Application.Authorization;
using Explore.Application.Models.Storage;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StorageObjects.Requests.Commands;

[AuthorizeResource(ResourceKinds.StorageObject, AuthorizationActions.Create)]
public sealed record FinalizeStorageUploadSessionCommand : ICommand<StorageUploadSessionResult>, ISecureRequest
{
    public Guid UploadSessionId { get; init; }
    public required Stream Content { get; init; }
    public string? ContentType { get; init; }
    public long? ContentLength { get; init; }
    public Guid? TenantId { get; init; }

    string? ISecureRequest.ResourceId => UploadSessionId == Guid.Empty ? null : UploadSessionId.ToString();

}
