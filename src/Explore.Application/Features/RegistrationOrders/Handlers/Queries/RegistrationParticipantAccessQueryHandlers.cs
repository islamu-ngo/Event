using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Handlers;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Queries;

public sealed class GetGuestRegistrationOrderParticipantsQueryHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    IQueryHandler<GetRegistrationOrderParticipantsQuery, RegistrationOrderParticipantsDto?> participantsHandler)
    : IQueryHandler<GetGuestRegistrationOrderParticipantsQuery, RegistrationOrderParticipantsDto?>
{
    public async Task<RegistrationOrderParticipantsDto?> QueryAsync(
        GetGuestRegistrationOrderParticipantsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (await RegistrationOrderAccessGuard.GetGuestOrderAsync(
                inventory, capabilities, tenant.TenantId, query.EventId, query.OrderId,
                query.CapabilityToken, timeProvider, cancellationToken) is null)
        {
            return null;
        }

        RegistrationOrderParticipantsDto? result = await participantsHandler.QueryAsync(
            new GetRegistrationOrderParticipantsQuery(query.OrderId), cancellationToken);
        return result is null ? null : result with { CanManage = true };
    }
}

public sealed class GetAuthenticatedRegistrationOrderParticipantsQueryHandler(
    IRegistrationInventoryRepository inventory,
    IEventRepository events,
    ICurrentUserService currentUser,
    ITenantContext tenant,
    IAuthorizationProvider authorization,
    IQueryHandler<GetRegistrationOrderParticipantsQuery, RegistrationOrderParticipantsDto?> participantsHandler)
    : IQueryHandler<GetAuthenticatedRegistrationOrderParticipantsQuery, RegistrationOrderParticipantsDto?>
{
    public async Task<RegistrationOrderParticipantsDto?> QueryAsync(
        GetAuthenticatedRegistrationOrderParticipantsQuery query,
        CancellationToken cancellationToken = default)
    {
        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(query.OrderId, tenant.TenantId, cancellationToken);
        if (order is null || order.EventId != query.EventId)
        {
            return null;
        }

        bool ownsOrder = currentUser.IsAuthenticated && currentUser.UserId == order.AccountUserId;
        bool organizerMayManage = await OrganizerMayViewAsync(order, cancellationToken);
        if (!ownsOrder && !organizerMayManage)
        {
            return null;
        }

        RegistrationOrderParticipantsDto? result = await participantsHandler.QueryAsync(
            new GetRegistrationOrderParticipantsQuery(order.Id), cancellationToken);
        return result is null ? null : result with { CanManage = ownsOrder, CanImportCompanyCsv = organizerMayManage };
    }

    private async Task<bool> OrganizerMayViewAsync(RegistrationOrder order, CancellationToken cancellationToken)
    {
        Event? eventEntity = await events.GetAuthorizationTargetByIdAsync(order.EventId, cancellationToken);
        if (eventEntity?.TenantId != order.TenantId ||
            eventEntity.ParticipationConfiguration?.ParticipationHandlingModeId != (int)ParticipationHandlingModeEnum.PlatformManaged)
        {
            return false;
        }

        var decision = await authorization.AuthorizeAsync(
            new AuthorizationRequest(
                AuthorizationCapabilityCatalog.Require(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrations),
                eventEntity.Id.ToString("D"),
                Scope: ResourceDescriptors.EventAuthorizationTarget.GetScope(eventEntity),
                Facts: ResourceDescriptors.EventAuthorizationTarget.GetFacts(eventEntity)),
            cancellationToken);
        return decision.IsAllowed;
    }
}
