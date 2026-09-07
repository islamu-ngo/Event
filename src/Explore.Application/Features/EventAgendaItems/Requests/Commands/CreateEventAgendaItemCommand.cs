using Explore.Application.Authorization;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventAgendaItems.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventAgendaItem, AuthorizationActions.Create)]
public sealed record CreateEventAgendaItemCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventAgendaItemDto EventAgendaItemDto { get; init; }

    string? ISecureRequest.ResourceId => null;
}
