using System;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Locations.Requests.Commands;

[AuthorizeResource(ResourceKinds.Location, AuthorizationActions.Delete)]
public sealed record DeleteLocationCommand : ICommand<bool>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
