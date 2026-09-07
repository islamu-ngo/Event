using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventAgendaItems.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventAgendaItem, AuthorizationActions.Delete)]
public sealed record DeleteEventAgendaItemCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
