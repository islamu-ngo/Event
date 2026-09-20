using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Authorization;
using Explore.Application.Features.RegistrationOrders.Handlers;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public interface IRegistrationParticipantMutationDispatcher
{
    Task<BaseCommandResponse<Guid>> DispatchMutationAsync(
        IRegistrationParticipantMutation mutation,
        CancellationToken cancellationToken = default);
}

public sealed class RegistrationParticipantMutationDispatcher(
    ICommandHandler<AddRegistrationParticipantCommand, BaseCommandResponse<Guid>> addHandler,
    ICommandHandler<UpdateRegistrationParticipantCommand, BaseCommandResponse<Guid>> updateHandler,
    ICommandHandler<AssignRegistrationTicketCommand, BaseCommandResponse<Guid>> assignHandler,
    ICommandHandler<BulkAssignRegistrationTicketsCommand, BaseCommandResponse<Guid>> bulkAssignHandler,
    ICommandHandler<DeferRegistrationTicketCommand, BaseCommandResponse<Guid>> deferHandler,
    ICommandHandler<BulkDeferRegistrationTicketsCommand, BaseCommandResponse<Guid>> bulkDeferHandler)
    : IRegistrationParticipantMutationDispatcher
{
    public Task<BaseCommandResponse<Guid>> DispatchMutationAsync(
        IRegistrationParticipantMutation mutation,
        CancellationToken cancellationToken = default) =>
        mutation switch
        {
            AddRegistrationParticipantCommand add => addHandler.ExecuteAsync(add, cancellationToken),
            UpdateRegistrationParticipantCommand update => updateHandler.ExecuteAsync(update, cancellationToken),
            AssignRegistrationTicketCommand assign => assignHandler.ExecuteAsync(assign, cancellationToken),
            BulkAssignRegistrationTicketsCommand bulkAssign => bulkAssignHandler.ExecuteAsync(bulkAssign, cancellationToken),
            DeferRegistrationTicketCommand defer => deferHandler.ExecuteAsync(defer, cancellationToken),
            BulkDeferRegistrationTicketsCommand bulkDefer => bulkDeferHandler.ExecuteAsync(bulkDefer, cancellationToken),
            _ => throw new NotSupportedException($"Unknown registration participant mutation type '{mutation.GetType().FullName}'.")
        };
}

public sealed class MutateGuestRegistrationParticipantsCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    IRegistrationParticipantMutationDispatcher dispatcher)
    : ICommandHandler<MutateGuestRegistrationParticipantsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        MutateGuestRegistrationParticipantsCommand command,
        CancellationToken cancellationToken = default) =>
        command.Mutation.RegistrationOrderId != command.OrderId ||
        await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory, capabilities, tenant.TenantId, command.EventId, command.OrderId,
            command.CapabilityToken, timeProvider, cancellationToken) is null
            ? RegistrationOrderAccessGuard.ParticipantNotFound(command.OrderId)
            : await dispatcher.DispatchMutationAsync(command.Mutation, cancellationToken);
}

public sealed class MutateAuthenticatedRegistrationParticipantsCommandHandler(
    IRegistrationInventoryRepository inventory,
    IEventRepository events,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    IAuthorizationProvider authorization,
    IRegistrationParticipantMutationDispatcher dispatcher)
    : ICommandHandler<MutateAuthenticatedRegistrationParticipantsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        MutateAuthenticatedRegistrationParticipantsCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Mutation.RegistrationOrderId != command.OrderId)
        {
            return RegistrationOrderAccessGuard.ParticipantNotFound(command.OrderId);
        }

        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(command.OrderId, tenant.TenantId, cancellationToken);
        if (order is null || order.EventId != command.EventId)
        {
            return RegistrationOrderAccessGuard.ParticipantNotFound(command.OrderId);
        }

        bool ownsOrder = currentUser.IsAuthenticated && currentUser.UserId == order.AccountUserId;
        return ownsOrder
            ? await dispatcher.DispatchMutationAsync(command.Mutation, cancellationToken)
            : RegistrationOrderAccessGuard.ParticipantNotFound(command.OrderId);
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
