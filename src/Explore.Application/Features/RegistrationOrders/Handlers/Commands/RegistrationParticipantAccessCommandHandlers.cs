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
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class MutateGuestRegistrationParticipantsCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    IServiceProvider serviceProvider)
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
            : await DispatchMutationAsync(command.Mutation, serviceProvider, cancellationToken);

    internal static Task<BaseCommandResponse<Guid>> DispatchMutationAsync(
        IRegistrationParticipantMutation mutation,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken) =>
        mutation switch
        {
            AddRegistrationParticipantCommand add => serviceProvider.GetRequiredService<ICommandHandler<AddRegistrationParticipantCommand, BaseCommandResponse<Guid>>>().ExecuteAsync(add, cancellationToken),
            UpdateRegistrationParticipantCommand update => serviceProvider.GetRequiredService<ICommandHandler<UpdateRegistrationParticipantCommand, BaseCommandResponse<Guid>>>().ExecuteAsync(update, cancellationToken),
            AssignRegistrationTicketCommand assign => serviceProvider.GetRequiredService<ICommandHandler<AssignRegistrationTicketCommand, BaseCommandResponse<Guid>>>().ExecuteAsync(assign, cancellationToken),
            BulkAssignRegistrationTicketsCommand bulkAssign => serviceProvider.GetRequiredService<ICommandHandler<BulkAssignRegistrationTicketsCommand, BaseCommandResponse<Guid>>>().ExecuteAsync(bulkAssign, cancellationToken),
            DeferRegistrationTicketCommand defer => serviceProvider.GetRequiredService<ICommandHandler<DeferRegistrationTicketCommand, BaseCommandResponse<Guid>>>().ExecuteAsync(defer, cancellationToken),
            BulkDeferRegistrationTicketsCommand bulkDefer => serviceProvider.GetRequiredService<ICommandHandler<BulkDeferRegistrationTicketsCommand, BaseCommandResponse<Guid>>>().ExecuteAsync(bulkDefer, cancellationToken),
            _ => throw new NotSupportedException($"Unknown registration participant mutation type '{mutation.GetType().FullName}'.")
        };
}

public sealed class MutateAuthenticatedRegistrationParticipantsCommandHandler(
    IRegistrationInventoryRepository inventory,
    IEventRepository events,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    IAuthorizationProvider authorization,
    IServiceProvider serviceProvider)
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
            ? await MutateGuestRegistrationParticipantsCommandHandler.DispatchMutationAsync(command.Mutation, serviceProvider, cancellationToken)
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
