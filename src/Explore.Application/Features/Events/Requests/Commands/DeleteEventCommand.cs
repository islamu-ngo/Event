using System;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Events.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Delete)]
public sealed record DeleteEventCommand : ICommand<bool>, ISecureRequest
{
    public Guid Id { get; init; }
    public required string UserId { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
