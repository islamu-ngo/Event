using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventTicketing.Handlers.Commands;

public sealed class CloneEventTicketCatalogDraftCommandHandler(
    IEventRepository events,
    IEventTicketCatalogRepository catalogs,
    ITenantContext tenant) : IRequestHandler<CloneEventTicketCatalogDraftCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        CloneEventTicketCatalogDraftCommand request,
        CancellationToken cancellationToken)
    {
        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken);
        if (!IsPlatformManaged(eventTarget, tenant.TenantId))
        {
            return Missing(request.EventId);
        }

        EventTicketCatalogVersion? publishedCatalog = await catalogs.GetPublishedCatalogAsync(
            request.EventId,
            tenant.TenantId,
            cancellationToken);
        if (publishedCatalog is null)
        {
            return Missing(request.EventId);
        }

        EventTicketCatalogVersion? managementCatalog = await catalogs.GetManagementCatalogAsync(
            request.EventId,
            tenant.TenantId,
            cancellationToken);
        if (managementCatalog?.TicketCatalogStatusId == (int)TicketCatalogStatusEnum.Draft)
        {
            return Bad(request.EventId, "A ticket catalog draft already exists.");
        }

        EventTicketCatalogVersion draft;
        try
        {
            draft = publishedCatalog.CloneToDraft();
        }
        catch (InvalidOperationException exception)
        {
            return Bad(request.EventId, exception.Message);
        }

        try
        {
            await catalogs.AddAsync(draft, cancellationToken);
            return Ok(draft.Id, "Ticket catalog draft cloned.");
        }
        catch (ConcurrencyConflictException exception)
        {
            return Conflict(request.EventId, exception.Message);
        }
    }

    private static bool IsPlatformManaged(Event? eventTarget, Guid tenantId) =>
        eventTarget?.TenantId == tenantId
        && eventTarget.ParticipationConfiguration?.ParticipationHandlingModeId == (int)ParticipationHandlingModeEnum.PlatformManaged;

    private static BaseCommandResponse<Guid> Ok(Guid id, string message) => BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Missing(Guid id) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_not_found", "Ticketing configuration was not found.", ["Ticketing configuration was not found."], id);

    private static BaseCommandResponse<Guid> Bad(Guid id, string error) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_validation_failed", "Ticketing configuration is invalid.", [error], id);

    private static BaseCommandResponse<Guid> Conflict(Guid id, string error) => BaseCommandResponse.Failure<Guid>(
        "event_ticketing_concurrency_conflict", "Ticketing configuration was updated by another request.", [error], id);
}
