using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventDays.Requests.Commands;

namespace Explore.Application.Features.EventDays.Authorization;

/// <summary>
/// Binds day writes to the persisted parent in the current tenant. A submitted
/// destination never substitutes for source authority; the validator rejects reparenting.
/// </summary>
public sealed class EventDayAuthorizationContextEnricher(
    IEventDayRepository days,
    IEventRepository events,
    ITenantContext tenantContext)
    : IAuthorizationContextEnricher<CreateEventDayCommand>,
      IAuthorizationContextEnricher<UpdateEventDayCommand>,
      IAuthorizationContextEnricher<DeleteEventDayCommand>
{
    public async Task<AuthorizationContext> ResolveAsync(CreateEventDayCommand request, CancellationToken cancellationToken) =>
        new(null, await RequireParentAsync(request.EventDayDto.EventId, AuthorizationActions.Create, cancellationToken));

    public Task<AuthorizationContext> ResolveAsync(UpdateEventDayCommand request, CancellationToken cancellationToken) =>
        ResolveDayAsync(request.EventDayId, AuthorizationActions.Update, cancellationToken);

    public Task<AuthorizationContext> ResolveAsync(DeleteEventDayCommand request, CancellationToken cancellationToken) =>
        ResolveDayAsync(request.Id, AuthorizationActions.Delete, cancellationToken);

    private async Task<AuthorizationContext> ResolveDayAsync(Guid id, string action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var day = await days.GetById(id);
        if (day is null || day.IsDeleted || day.TenantId != tenantContext.TenantId)
            throw new AuthorizationException(ResourceKinds.EventDay, action);
        return new AuthorizationContext(id.ToString(), await RequireParentAsync(day.EventId, action, cancellationToken));
    }

    private async Task<EventScopedAuthorizationFacts> RequireParentAsync(Guid eventId, string action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parent = await events.GetById(eventId);
        cancellationToken.ThrowIfCancellationRequested();
        if (parent is null || parent.IsDeleted || parent.TenantId != tenantContext.TenantId)
            throw new AuthorizationException(ResourceKinds.EventDay, action);
        return new EventScopedAuthorizationFacts(parent.TenantId, parent.Id);
    }
}
