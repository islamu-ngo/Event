using Explore.Application.Authorization;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Requests.Commands;

[AuthorizeResource(ResourceKinds.StorageObject, AuthorizationActions.Create)]
public sealed record CreateStorageUploadSessionCommand : IRequest<BaseCommandResponse<StorageUploadSessionDto>>, ISecureRequest
{
    public required CreateStorageUploadSessionDto UploadSessionDto { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => UploadSessionDto.OwningResourceId?.ToString();

    // Upload intent facts are established server-side by CreateStorageUploadSessionAuthorizationContextEnricher,
    // which loads the owning resource. The request declares none: a requested owner is not evidence of one.
}
