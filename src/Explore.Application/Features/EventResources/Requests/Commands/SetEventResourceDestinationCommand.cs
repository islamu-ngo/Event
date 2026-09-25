using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventResources.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventResource, "update")]
public sealed record SetEventResourceDestinationCommand(Guid ResourceId, Guid ExpectedVersion, string Destination)
    : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string ISecureRequest.ResourceId => ResourceId.ToString("D");
    public override string ToString() => nameof(SetEventResourceDestinationCommand);
}
