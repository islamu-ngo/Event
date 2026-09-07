using System;
using Explore.Application.Authorization;
using MediatR;

namespace Explore.Application.Features.EventTags.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record DeleteEventTagsCommand : IRequest<bool>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
