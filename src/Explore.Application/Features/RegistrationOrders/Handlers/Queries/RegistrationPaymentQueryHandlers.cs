using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Features.RegistrationOrders.Validators;
using Explore.Application.Services.Registration;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Queries;

public sealed class GetGuestRegistrationPaymentQueryHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    RegistrationPaymentContractService payments)
    : IRequestHandler<GetGuestRegistrationPaymentQuery, RegistrationPaymentDto?>
{
    public async Task<RegistrationPaymentDto?> Handle(GetGuestRegistrationPaymentQuery request, CancellationToken cancellationToken)
    {
        var validator = new GuestRegistrationOrderAccessCommandValidator<GetGuestRegistrationPaymentQuery>();
        if (!(await validator.ValidateAsync(request, cancellationToken)).IsValid)
        {
            return null;
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId, request.CapabilityToken, timeProvider, cancellationToken);
        return order is null ? null : await payments.GetAsync(order, cancellationToken);
    }
}

public sealed class GetAuthenticatedRegistrationPaymentQueryHandler(
    IRegistrationInventoryRepository inventory,
    IEventRepository events,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    RegistrationPaymentContractService payments)
    : IRequestHandler<GetAuthenticatedRegistrationPaymentQuery, RegistrationPaymentDto?>
{
    public async Task<RegistrationPaymentDto?> Handle(GetAuthenticatedRegistrationPaymentQuery request, CancellationToken cancellationToken)
    {
        var validator = new AuthenticatedRegistrationOrderAccessCommandValidator<GetAuthenticatedRegistrationPaymentQuery>();
        if (!(await validator.ValidateAsync(request, cancellationToken)).IsValid)
        {
            return null;
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
            inventory, currentUser, tenant.TenantId, request.EventId, request.OrderId, cancellationToken);
        Explore.Domain.Event? @event = order is null ? null : await events.GetById(request.EventId);
        return order is null || @event?.TenantId != tenant.TenantId
            ? null
            : await payments.GetAsync(
                order,
                cancellationToken,
                buyerRefundAllowed: @event.EventStatusId == (int)Explore.Domain.Enums.EventStatusEnum.Cancelled);
    }
}

public sealed class GetGuestPaidOrderAcceptanceQueryHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    RegistrationPaymentContractService payments)
    : IRequestHandler<GetGuestPaidOrderAcceptanceQuery, PaidOrderAcceptanceDisclosureDto?>
{
    public async Task<PaidOrderAcceptanceDisclosureDto?> Handle(GetGuestPaidOrderAcceptanceQuery request, CancellationToken cancellationToken)
    {
        var validator = new GuestRegistrationOrderAccessCommandValidator<GetGuestPaidOrderAcceptanceQuery>();
        if (!(await validator.ValidateAsync(request, cancellationToken)).IsValid)
        {
            return null;
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId, request.CapabilityToken, timeProvider, cancellationToken);
        return order is null ? null : (await payments.GetAcceptanceDisclosureAsync(order, cancellationToken)).Disclosure;
    }
}

public sealed class GetAuthenticatedPaidOrderAcceptanceQueryHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    RegistrationPaymentContractService payments)
    : IRequestHandler<GetAuthenticatedPaidOrderAcceptanceQuery, PaidOrderAcceptanceDisclosureDto?>
{
    public async Task<PaidOrderAcceptanceDisclosureDto?> Handle(GetAuthenticatedPaidOrderAcceptanceQuery request, CancellationToken cancellationToken)
    {
        var validator = new AuthenticatedRegistrationOrderAccessCommandValidator<GetAuthenticatedPaidOrderAcceptanceQuery>();
        if (!(await validator.ValidateAsync(request, cancellationToken)).IsValid)
        {
            return null;
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
            inventory, currentUser, tenant.TenantId, request.EventId, request.OrderId, cancellationToken);
        return order is null ? null : (await payments.GetAcceptanceDisclosureAsync(order, cancellationToken)).Disclosure;
    }
}

public sealed class GetGuestRegistrationPaymentCheckoutTargetQueryHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    RegistrationPaymentContractService payments)
    : IRequestHandler<GetGuestRegistrationPaymentCheckoutTargetQuery, RegistrationPaymentCheckoutTargetDto?>
{
    public async Task<RegistrationPaymentCheckoutTargetDto?> Handle(GetGuestRegistrationPaymentCheckoutTargetQuery request, CancellationToken cancellationToken)
    {
        var validator = new GuestRegistrationOrderAccessCommandValidator<GetGuestRegistrationPaymentCheckoutTargetQuery>();
        if (!(await validator.ValidateAsync(request, cancellationToken)).IsValid)
        {
            return null;
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId, request.CapabilityToken, timeProvider, cancellationToken);
        return order is null ? null : await payments.ResolveCheckoutTargetAsync(order, cancellationToken);
    }
}

public sealed class GetAuthenticatedRegistrationPaymentCheckoutTargetQueryHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    RegistrationPaymentContractService payments)
    : IRequestHandler<GetAuthenticatedRegistrationPaymentCheckoutTargetQuery, RegistrationPaymentCheckoutTargetDto?>
{
    public async Task<RegistrationPaymentCheckoutTargetDto?> Handle(GetAuthenticatedRegistrationPaymentCheckoutTargetQuery request, CancellationToken cancellationToken)
    {
        var validator = new AuthenticatedRegistrationOrderAccessCommandValidator<GetAuthenticatedRegistrationPaymentCheckoutTargetQuery>();
        if (!(await validator.ValidateAsync(request, cancellationToken)).IsValid)
        {
            return null;
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetCurrentAccountOrderBeforeExpiryAsync(
            inventory, currentUser, tenant.TenantId, request.EventId, request.OrderId, timeProvider, cancellationToken);
        return order is null ? null : await payments.ResolveCheckoutTargetAsync(order, cancellationToken);
    }
}

public sealed class GetStudioRegistrationPaymentQueryHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    RegistrationPaymentContractService payments)
    : IRequestHandler<GetStudioRegistrationPaymentQuery, RegistrationPaymentDto?>
{
    public async Task<RegistrationPaymentDto?> Handle(GetStudioRegistrationPaymentQuery request, CancellationToken cancellationToken)
    {
        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(request.OrderId, tenant.TenantId, cancellationToken);
        return order?.EventId != request.EventId
            ? null
            : await payments.GetAsync(order, cancellationToken, organizerRefundAllowed: true);
    }
}
