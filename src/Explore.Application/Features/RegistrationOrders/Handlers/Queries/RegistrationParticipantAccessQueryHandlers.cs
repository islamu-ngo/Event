using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Handlers;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Queries;

public sealed class GetGuestRegistrationOrderParticipantsQueryHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    ISender sender)
    : IRequestHandler<GetGuestRegistrationOrderParticipantsQuery, RegistrationOrderParticipantsDto?>
{
    public async Task<RegistrationOrderParticipantsDto?> Handle(
        GetGuestRegistrationOrderParticipantsQuery request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetGuestOrderAsync(
                inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId,
                request.CapabilityToken, timeProvider, cancellationToken) is null)
        {
            return null;
        }

        RegistrationOrderParticipantsDto? result = await sender.Send(
            new GetRegistrationOrderParticipantsQuery(request.OrderId), cancellationToken);
        return result is null ? null : result with { CanManage = true };
    }
}

public sealed class GetAuthenticatedRegistrationOrderParticipantsQueryHandler(
    IRegistrationInventoryRepository inventory,
    IEventRepository events,
    ICurrentUserService currentUser,
    ITenantContext tenant,
    IAuthorizationProvider authorization,
    ISender sender)
    : IRequestHandler<GetAuthenticatedRegistrationOrderParticipantsQuery, RegistrationOrderParticipantsDto?>
{
    public async Task<RegistrationOrderParticipantsDto?> Handle(
        GetAuthenticatedRegistrationOrderParticipantsQuery request,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(request.OrderId, tenant.TenantId, cancellationToken);
        if (order is null || order.EventId != request.EventId)
        {
            return null;
        }

        bool ownsOrder = currentUser.IsAuthenticated && currentUser.UserId == order.AccountUserId;
        bool organizerMayManage = await OrganizerMayViewAsync(order, cancellationToken);
        if (!ownsOrder && !organizerMayManage)
        {
            return null;
        }

        RegistrationOrderParticipantsDto? result = await sender.Send(
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
