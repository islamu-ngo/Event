using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Authorization;
using Explore.Application.Features.RegistrationOrders.Handlers;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class MutateGuestRegistrationParticipantsCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    ISender sender)
    : IRequestHandler<MutateGuestRegistrationParticipantsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        MutateGuestRegistrationParticipantsCommand request,
        CancellationToken cancellationToken) =>
        request.Mutation.RegistrationOrderId != request.OrderId ||
        await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId,
            request.CapabilityToken, timeProvider, cancellationToken) is null
            ? RegistrationOrderAccessGuard.ParticipantNotFound(request.OrderId)
            : await sender.Send(request.Mutation, cancellationToken);
}

public sealed class MutateAuthenticatedRegistrationParticipantsCommandHandler(
    IRegistrationInventoryRepository inventory,
    IEventRepository events,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    IAuthorizationProvider authorization,
    ISender sender)
    : IRequestHandler<MutateAuthenticatedRegistrationParticipantsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        MutateAuthenticatedRegistrationParticipantsCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Mutation.RegistrationOrderId != request.OrderId)
        {
            return RegistrationOrderAccessGuard.ParticipantNotFound(request.OrderId);
        }

        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(request.OrderId, tenant.TenantId, cancellationToken);
        if (order is null || order.EventId != request.EventId)
        {
            return RegistrationOrderAccessGuard.ParticipantNotFound(request.OrderId);
        }

        bool ownsOrder = currentUser.IsAuthenticated && currentUser.UserId == order.AccountUserId;
        return ownsOrder
            ? await sender.Send(request.Mutation, cancellationToken)
            : RegistrationOrderAccessGuard.ParticipantNotFound(request.OrderId);
    }

    private async Task<bool> OrganizerMayManageAsync(RegistrationOrder order, CancellationToken cancellationToken)
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
