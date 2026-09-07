using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionAgendaItem;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSessionAgendaItem, AuthorizationActions.Create)]
public sealed record CreateEventSessionAgendaItemCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventSessionAgendaItemDto AgendaItemDto { get; init; }

    string? ISecureRequest.ResourceId => AgendaItemDto.EventSessionId.ToString();
}
