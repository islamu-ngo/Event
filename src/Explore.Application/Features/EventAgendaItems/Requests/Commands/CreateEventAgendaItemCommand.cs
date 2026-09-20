using Explore.Application.Authorization;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventAgendaItems.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventAgendaItem, AuthorizationActions.Create)]
public sealed record CreateEventAgendaItemCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventAgendaItemDto EventAgendaItemDto { get; init; }

    string? ISecureRequest.ResourceId => null;
}
