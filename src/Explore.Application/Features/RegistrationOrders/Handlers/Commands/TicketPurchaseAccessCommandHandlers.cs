using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class ReserveAuthenticatedTicketPurchaseCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITicketPurchaseGovernanceRepository governance,
    ICurrentUserService currentUser,
    ITenantContext tenant,
    ICommandHandler<ReserveTicketPurchaseCommand, BaseCommandResponse<Guid>> reserveHandler) :
    ICommandHandler<
        ReserveAuthenticatedTicketPurchaseCommand,
        BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ReserveAuthenticatedTicketPurchaseCommand command,
        CancellationToken cancellationToken = default)
    {
        RegistrationOrder? order =
            await RegistrationOrderAccessGuard
                .GetCurrentAccountOrderAsync(
                    inventory,
                    currentUser,
                    tenant.TenantId,
                    command.EventId,
                    command.OrderId,
                    cancellationToken);
        if (order is null)
        {
            return NotFound(command.OrderId);
        }

        TicketPurchasePolicyVersion? policy =
            await governance.GetCurrentPolicyVersionAsync(
                tenant.TenantId,
                command.EventId,
                cancellationToken);
        if (policy is null)
        {
            return PolicyUnavailable(command.OrderId);
        }

        return await reserveHandler.ExecuteAsync(
            new ReserveTicketPurchaseCommand(
                command.EventId,
                command.OrderId,
                policy.Id,
                TicketPurchaseAccessMode.AuthenticatedAccount,
                command.RequestedPurchaserActorId,
                command.OperationKey),
            cancellationToken);
    }

    private static BaseCommandResponse<Guid> NotFound(
        Guid orderId) =>
        BaseCommandResponse.Failure<Guid>(
            "registration_order_not_found",
            "Registration order was not found.",
            id: orderId);

    private static BaseCommandResponse<Guid>
        PolicyUnavailable(Guid orderId) =>
        BaseCommandResponse.Failure<Guid>(
            TicketPurchaseFailureCodes.PolicyUnavailable,
            id: orderId);
}

public sealed class ReserveGuestTicketPurchaseCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITicketPurchaseGovernanceRepository governance,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    ICommandHandler<ReserveTicketPurchaseCommand, BaseCommandResponse<Guid>> reserveHandler) :
    ICommandHandler<
        ReserveGuestTicketPurchaseCommand,
        BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ReserveGuestTicketPurchaseCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.AccessMode is not (
            TicketPurchaseAccessMode.VerifiedContact
            or TicketPurchaseAccessMode.NameOnly))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Guest purchase access mode is invalid."],
                id: command.OrderId);
        }

        RegistrationOrder? order =
            await RegistrationOrderAccessGuard.GetGuestOrderAsync(
                inventory,
                capabilities,
                tenant.TenantId,
                command.EventId,
                command.OrderId,
                command.CapabilityToken,
                timeProvider,
                cancellationToken);
        if (order is null)
        {
            return NotFound(command.OrderId);
        }

        TicketPurchasePolicyVersion? policy =
            await governance.GetCurrentPolicyVersionAsync(
                tenant.TenantId,
                command.EventId,
                cancellationToken);
        if (policy is null)
        {
            return PolicyUnavailable(command.OrderId);
        }

        return await reserveHandler.ExecuteAsync(
            new ReserveTicketPurchaseCommand(
                command.EventId,
                command.OrderId,
                policy.Id,
                command.AccessMode,
                RequestedPurchaserActorId: null,
                command.OperationKey),
            cancellationToken);
    }

    private static BaseCommandResponse<Guid> NotFound(
        Guid orderId) =>
        BaseCommandResponse.Failure<Guid>(
            "registration_order_not_found",
            "Registration order was not found.",
            id: orderId);

    private static BaseCommandResponse<Guid>
        PolicyUnavailable(Guid orderId) =>
        BaseCommandResponse.Failure<Guid>(
            TicketPurchaseFailureCodes.PolicyUnavailable,
            id: orderId);
}
