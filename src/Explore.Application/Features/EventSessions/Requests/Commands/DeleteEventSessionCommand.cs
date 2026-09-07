using System;
using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessions.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Delete)]
public sealed record DeleteEventSessionCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
