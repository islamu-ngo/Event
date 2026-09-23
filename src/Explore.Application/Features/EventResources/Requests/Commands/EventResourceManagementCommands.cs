using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventResources.Requests.Commands;

/// <summary>ResourceId is a caller-retained UUIDv7 replay identity, not tenant or subject authority.</summary>
[AuthorizeResource(ResourceKinds.EventResource, "create")]
public sealed record CreateEventResourceCommand(Guid EventId, Guid ResourceId, EventResourceDraftDto Draft)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string ISecureRequest.ResourceId => EventId.ToString("D");
    public override string ToString() => nameof(CreateEventResourceCommand);
}

[AuthorizeResource(ResourceKinds.EventResource, "update")]
public sealed record UpdateEventResourceCommand(Guid ResourceId, Guid ExpectedVersion, EventResourceDraftDto Draft)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string ISecureRequest.ResourceId => ResourceId.ToString("D");
    public override string ToString() => nameof(UpdateEventResourceCommand);
}

[AuthorizeResource(ResourceKinds.EventResource, "publish")]
public sealed record PublishEventResourceCommand(Guid ResourceId, Guid ExpectedVersion)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string ISecureRequest.ResourceId => ResourceId.ToString("D");
}

[AuthorizeResource(ResourceKinds.EventResource, "unpublish")]
public sealed record UnpublishEventResourceCommand(Guid ResourceId, Guid ExpectedVersion)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string ISecureRequest.ResourceId => ResourceId.ToString("D");
}

[AuthorizeResource(ResourceKinds.EventResource, "archive")]
public sealed record ArchiveEventResourceCommand(Guid ResourceId, Guid ExpectedVersion)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string ISecureRequest.ResourceId => ResourceId.ToString("D");
}

[AuthorizeResource(ResourceKinds.EventResource, "delete")]
public sealed record DeleteEventResourceCommand(Guid ResourceId, Guid ExpectedVersion)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string ISecureRequest.ResourceId => ResourceId.ToString("D");
}

[AuthorizeResource(ResourceKinds.EventResource, "moderate")]
public sealed record ModerateEventResourceCommand(Guid ResourceId, Guid ExpectedVersion)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string ISecureRequest.ResourceId => ResourceId.ToString("D");
}
