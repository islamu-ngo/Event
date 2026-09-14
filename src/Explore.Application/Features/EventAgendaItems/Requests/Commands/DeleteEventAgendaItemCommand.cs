using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventAgendaItems.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventAgendaItem, AuthorizationActions.Delete)]
public sealed record DeleteEventAgendaItemCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
