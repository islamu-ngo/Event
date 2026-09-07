using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessions.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Create)]
public sealed record CreateEventSessionCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventSessionDto EventSessionDto { get; init; }

    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => EventSessionDto.EventId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new PreCreateAuthorizationFacts(TenantId, EventSessionDto.EventId, null, null);
}
