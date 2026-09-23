using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventResource;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventResources.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventResource, "update")]
public sealed record CreateEventResourceUploadSessionCommand(Guid ResourceId, CreateEventResourceUploadSessionDto UploadSessionDto)
    : ICommand<BaseCommandResponse<StorageUploadSessionDto>>, ISecureRequest
{
    string ISecureRequest.ResourceId => ResourceId.ToString("D");
    public override string ToString() => nameof(CreateEventResourceUploadSessionCommand);
}
