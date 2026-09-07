using Explore.Application.Authorization;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Requests.Commands;

[AuthorizeResource(ResourceKinds.StorageObject, AuthorizationActions.Delete)]
public sealed record CancelStorageUploadSessionCommand : IRequest<BaseCommandResponse<StorageUploadSessionDto>>, ISecureRequest
{
    public Guid UploadSessionId { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => UploadSessionId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new StorageObjectCollectionAuthorizationFacts(TenantId);
}
