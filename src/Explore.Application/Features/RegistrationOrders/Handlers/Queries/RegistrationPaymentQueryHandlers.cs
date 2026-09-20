using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Features.RegistrationOrders.Validators;
using Explore.Application.Services.Registration;
using Explore.Domain;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Queries;

public sealed class GetGuestRegistrationPaymentQueryHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    RegistrationPaymentContractService payments)
    : IQueryHandler<GetGuestRegistrationPaymentQuery, RegistrationPaymentDto?>
{
    public async Task<RegistrationPaymentDto?> QueryAsync(GetGuestRegistrationPaymentQuery query, CancellationToken cancellationToken = default)
    {
        var validator = new GuestRegistrationOrderAccessCommandValidator<GetGuestRegistrationPaymentQuery>();
        if (!(await validator.ValidateAsync(query, cancellationToken)).IsValid)
        {
            return null;
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory, capabilities, tenant.TenantId, query.EventId, query.OrderId, query.CapabilityToken, timeProvider, cancellationToken);
        return order is null ? null : await payments.GetAsync(order, cancellationToken);
    }
}

public sealed class GetAuthenticatedRegistrationPaymentQueryHandler(
    IRegistrationInventoryRepository inventory,
    IEventRepository events,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    RegistrationPaymentContractService payments)
    : IQueryHandler<GetAuthenticatedRegistrationPaymentQuery, RegistrationPaymentDto?>
{
    public async Task<RegistrationPaymentDto?> QueryAsync(GetAuthenticatedRegistrationPaymentQuery query, CancellationToken cancellationToken = default)
    {
        var validator = new AuthenticatedRegistrationOrderAccessCommandValidator<GetAuthenticatedRegistrationPaymentQuery>();
        if (!(await validator.ValidateAsync(query, cancellationToken)).IsValid)
        {
            return null;
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
            inventory, currentUser, tenant.TenantId, query.EventId, query.OrderId, cancellationToken);
        Explore.Domain.Event? @event = order is null ? null : await events.GetById(query.EventId);
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
    : IQueryHandler<GetGuestPaidOrderAcceptanceQuery, PaidOrderAcceptanceDisclosureDto?>
{
    public async Task<PaidOrderAcceptanceDisclosureDto?> QueryAsync(GetGuestPaidOrderAcceptanceQuery query, CancellationToken cancellationToken = default)
    {
        var validator = new GuestRegistrationOrderAccessCommandValidator<GetGuestPaidOrderAcceptanceQuery>();
        if (!(await validator.ValidateAsync(query, cancellationToken)).IsValid)
        {
            return null;
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory, capabilities, tenant.TenantId, query.EventId, query.OrderId, query.CapabilityToken, timeProvider, cancellationToken);
        return order is null ? null : (await payments.GetAcceptanceDisclosureAsync(order, cancellationToken)).Disclosure;
    }
}

public sealed class GetAuthenticatedPaidOrderAcceptanceQueryHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    RegistrationPaymentContractService payments)
    : IQueryHandler<GetAuthenticatedPaidOrderAcceptanceQuery, PaidOrderAcceptanceDisclosureDto?>
{
    public async Task<PaidOrderAcceptanceDisclosureDto?> QueryAsync(GetAuthenticatedPaidOrderAcceptanceQuery query, CancellationToken cancellationToken = default)
    {
        var validator = new AuthenticatedRegistrationOrderAccessCommandValidator<GetAuthenticatedPaidOrderAcceptanceQuery>();
        if (!(await validator.ValidateAsync(query, cancellationToken)).IsValid)
        {
            return null;
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
            inventory, currentUser, tenant.TenantId, query.EventId, query.OrderId, cancellationToken);
        return order is null ? null : (await payments.GetAcceptanceDisclosureAsync(order, cancellationToken)).Disclosure;
    }
}

public sealed class GetGuestRegistrationPaymentCheckoutTargetQueryHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    RegistrationPaymentContractService payments)
    : IQueryHandler<GetGuestRegistrationPaymentCheckoutTargetQuery, RegistrationPaymentCheckoutTargetDto?>
{
    public async Task<RegistrationPaymentCheckoutTargetDto?> QueryAsync(GetGuestRegistrationPaymentCheckoutTargetQuery query, CancellationToken cancellationToken = default)
    {
        var validator = new GuestRegistrationOrderAccessCommandValidator<GetGuestRegistrationPaymentCheckoutTargetQuery>();
        if (!(await validator.ValidateAsync(query, cancellationToken)).IsValid)
        {
            return null;
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory, capabilities, tenant.TenantId, query.EventId, query.OrderId, query.CapabilityToken, timeProvider, cancellationToken);
        return order is null ? null : await payments.ResolveCheckoutTargetAsync(order, cancellationToken);
    }
}

public sealed class GetAuthenticatedRegistrationPaymentCheckoutTargetQueryHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    RegistrationPaymentContractService payments)
    : IQueryHandler<GetAuthenticatedRegistrationPaymentCheckoutTargetQuery, RegistrationPaymentCheckoutTargetDto?>
{
    public async Task<RegistrationPaymentCheckoutTargetDto?> QueryAsync(GetAuthenticatedRegistrationPaymentCheckoutTargetQuery query, CancellationToken cancellationToken = default)
    {
        var validator = new AuthenticatedRegistrationOrderAccessCommandValidator<GetAuthenticatedRegistrationPaymentCheckoutTargetQuery>();
        if (!(await validator.ValidateAsync(query, cancellationToken)).IsValid)
        {
            return null;
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetCurrentAccountOrderBeforeExpiryAsync(
            inventory, currentUser, tenant.TenantId, query.EventId, query.OrderId, timeProvider, cancellationToken);
        return order is null ? null : await payments.ResolveCheckoutTargetAsync(order, cancellationToken);
    }
}

public sealed class GetStudioRegistrationPaymentQueryHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    RegistrationPaymentContractService payments)
    : IQueryHandler<GetStudioRegistrationPaymentQuery, RegistrationPaymentDto?>
{
    public async Task<RegistrationPaymentDto?> QueryAsync(GetStudioRegistrationPaymentQuery query, CancellationToken cancellationToken = default)
    {
        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(query.OrderId, tenant.TenantId, cancellationToken);
        return order?.EventId != query.EventId
            ? null
            : await payments.GetAsync(order, cancellationToken, organizerRefundAllowed: true);
    }
}
