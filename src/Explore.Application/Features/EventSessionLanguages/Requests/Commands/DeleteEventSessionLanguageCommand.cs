using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionLanguages.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Update)]
public sealed record DeleteEventSessionLanguageCommand : ICommand<bool>, ISecureRequest
{
    public int Id { get; init; }

    public Guid EventSessionId { get; init; }

    string? ISecureRequest.ResourceId => EventSessionId.ToString();

}
