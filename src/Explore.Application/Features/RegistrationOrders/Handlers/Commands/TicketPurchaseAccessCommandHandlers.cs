using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class ReserveAuthenticatedTicketPurchaseCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITicketPurchaseGovernanceRepository governance,
    ICurrentUserService currentUser,
    ITenantContext tenant,
    IMediator mediator) :
    IRequestHandler<
        ReserveAuthenticatedTicketPurchaseCommand,
        BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ReserveAuthenticatedTicketPurchaseCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order =
            await RegistrationOrderAccessGuard
                .GetCurrentAccountOrderAsync(
                    inventory,
                    currentUser,
                    tenant.TenantId,
                    request.EventId,
                    request.OrderId,
                    cancellationToken);
        if (order is null)
        {
            return NotFound(request.OrderId);
        }

        TicketPurchasePolicyVersion? policy =
            await governance.GetCurrentPolicyVersionAsync(
                tenant.TenantId,
                request.EventId,
                cancellationToken);
        if (policy is null)
        {
            return PolicyUnavailable(request.OrderId);
        }

        return await mediator.Send(
            new ReserveTicketPurchaseCommand(
                request.EventId,
                request.OrderId,
                policy.Id,
                TicketPurchaseAccessMode.AuthenticatedAccount,
                request.RequestedPurchaserActorId,
                request.OperationKey),
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
    IMediator mediator) :
    IRequestHandler<
        ReserveGuestTicketPurchaseCommand,
        BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ReserveGuestTicketPurchaseCommand request,
        CancellationToken cancellationToken)
    {
        if (request.AccessMode is not (
            TicketPurchaseAccessMode.VerifiedContact
            or TicketPurchaseAccessMode.NameOnly))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Guest purchase access mode is invalid."],
                id: request.OrderId);
        }

        RegistrationOrder? order =
            await RegistrationOrderAccessGuard.GetGuestOrderAsync(
                inventory,
                capabilities,
                tenant.TenantId,
                request.EventId,
                request.OrderId,
                request.CapabilityToken,
                timeProvider,
                cancellationToken);
        if (order is null)
        {
            return NotFound(request.OrderId);
        }

        TicketPurchasePolicyVersion? policy =
            await governance.GetCurrentPolicyVersionAsync(
                tenant.TenantId,
                request.EventId,
                cancellationToken);
        if (policy is null)
        {
            return PolicyUnavailable(request.OrderId);
        }

        return await mediator.Send(
            new ReserveTicketPurchaseCommand(
                request.EventId,
                request.OrderId,
                policy.Id,
                request.AccessMode,
                RequestedPurchaserActorId: null,
                request.OperationKey),
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
