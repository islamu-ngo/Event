using Explore.Application.Authorization;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventAgendaItems.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventAgendaItem, AuthorizationActions.Update)]
public sealed record UpdateEventAgendaItemCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventAgendaItemId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public required UpdateEventAgendaItemDto EventAgendaItemDto { get; init; }

    string? ISecureRequest.ResourceId => EventAgendaItemId.ToString();
}
