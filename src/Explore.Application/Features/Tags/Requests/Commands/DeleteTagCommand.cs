using System;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Tags.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tag, AuthorizationActions.Delete)]
public sealed record DeleteTagCommand : ICommand<bool>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
