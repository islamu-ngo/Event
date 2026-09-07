using System;
using Explore.Application.Authorization;
using MediatR;

namespace Explore.Application.Features.Tags.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tag, AuthorizationActions.Delete)]
public sealed record DeleteTagCommand : IRequest<bool>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
