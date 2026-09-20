using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventAgendaItems.Requests.Commands;

namespace Explore.Application.Features.EventAgendaItems.Authorization;

/// <summary>Resolves source authority from the persisted agenda item, never the submitted destination.</summary>
public sealed class EventAgendaItemAuthorizationContextEnricher(
    IEventAgendaItemRepository items,
    IEventRepository events,
    ITenantContext tenantContext)
    : IAuthorizationContextEnricher<CreateEventAgendaItemCommand>,
      IAuthorizationContextEnricher<UpdateEventAgendaItemCommand>,
      IAuthorizationContextEnricher<DeleteEventAgendaItemCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(CreateEventAgendaItemCommand request, CancellationToken cancellationToken) =>
        new(null, await RequireParentAsync(request.EventAgendaItemDto.EventId, AuthorizationActions.Create, cancellationToken));

    public Task<AuthorizationContext> ResolveAsync(UpdateEventAgendaItemCommand request, CancellationToken cancellationToken) =>
        ResolveItemAsync(request.EventAgendaItemId, AuthorizationActions.Update, cancellationToken);

    public Task<AuthorizationContext> ResolveAsync(DeleteEventAgendaItemCommand request, CancellationToken cancellationToken) =>
        ResolveItemAsync(request.Id, AuthorizationActions.Delete, cancellationToken);

    private async Task<AuthorizationContext> ResolveItemAsync(Guid id, string action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = await items.GetById(id);
        if (item is null || item.IsDeleted || item.TenantId != tenantContext.TenantId)
            throw new AuthorizationException(ResourceKinds.EventAgendaItem, action);
        return new AuthorizationContext(id.ToString(), await RequireParentAsync(item.EventId, action, cancellationToken));
    }

    private async Task<EventScopedAuthorizationFacts> RequireParentAsync(Guid eventId, string action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parent = await events.GetById(eventId);
        cancellationToken.ThrowIfCancellationRequested();
        if (parent is null || parent.IsDeleted || parent.TenantId != tenantContext.TenantId)
            throw new AuthorizationException(ResourceKinds.EventAgendaItem, action);
        return new EventScopedAuthorizationFacts(parent.TenantId, parent.Id);
    }
}
