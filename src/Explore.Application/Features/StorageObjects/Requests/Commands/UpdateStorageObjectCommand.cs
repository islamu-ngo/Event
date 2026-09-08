using Explore.Application.Authorization;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Requests.Commands;

[AuthorizeResource(ResourceKinds.StorageObject, AuthorizationActions.Update)]
public sealed record UpdateStorageObjectCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid StorageObjectId { get; init; }
    public required UpdateStorageObjectDto StorageObjectDto { get; init; }

    string? ISecureRequest.ResourceId => StorageObjectId.ToString("D");
}
