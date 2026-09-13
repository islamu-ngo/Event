using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StorageObjects.Requests.Commands;

[AuthorizeResource(ResourceKinds.StorageObject, AuthorizationActions.Delete)]
public sealed record DeleteStorageObjectCommand : ICommand<bool>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
