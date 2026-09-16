using System;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionAgendaItems.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSessionAgendaItem, AuthorizationActions.Delete)]
public sealed record DeleteEventSessionAgendaItemCommand : ICommand<bool>, ISecureRequest
{
    public Guid Id { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}
