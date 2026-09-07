using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Responses;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Validators;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation.Results;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class StartGuestRegistrationPaymentCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    RegistrationPaymentContractService payments)
    : IRequestHandler<StartGuestRegistrationPaymentCommand, RegistrationPaymentCommandResultDto>
{
    public async Task<RegistrationPaymentCommandResultDto> Handle(StartGuestRegistrationPaymentCommand request, CancellationToken cancellationToken)
    {
        var validator = new GuestRegistrationOrderAccessCommandValidator<StartGuestRegistrationPaymentCommand>();
        if (!(await validator.ValidateAsync(request, cancellationToken)).IsValid)
        {
            return NotFound();
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId, request.CapabilityToken, timeProvider, cancellationToken);
        return order is null ? NotFound() : await payments.StartAsync(order, request.Acceptance, cancellationToken);
    }

    private static RegistrationPaymentCommandResultDto NotFound() => PaymentNotFound.Result();
}

public sealed class RetryGuestRegistrationPaymentCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    RegistrationPaymentContractService payments)
    : IRequestHandler<RetryGuestRegistrationPaymentCommand, RegistrationPaymentCommandResultDto>
{
    public async Task<RegistrationPaymentCommandResultDto> Handle(RetryGuestRegistrationPaymentCommand request, CancellationToken cancellationToken)
    {
        var validator = new GuestRegistrationOrderAccessCommandValidator<RetryGuestRegistrationPaymentCommand>();
        if (!(await validator.ValidateAsync(request, cancellationToken)).IsValid)
        {
            return PaymentNotFound.Result();
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId, request.CapabilityToken, timeProvider, cancellationToken);
        return order is null ? PaymentNotFound.Result() : await payments.RetryAsync(order, cancellationToken);
    }
}

public sealed class StartAuthenticatedRegistrationPaymentCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    RegistrationPaymentContractService payments)
    : IRequestHandler<StartAuthenticatedRegistrationPaymentCommand, RegistrationPaymentCommandResultDto>
{
    public async Task<RegistrationPaymentCommandResultDto> Handle(StartAuthenticatedRegistrationPaymentCommand request, CancellationToken cancellationToken)
    {
        var validator = new AuthenticatedRegistrationOrderAccessCommandValidator<StartAuthenticatedRegistrationPaymentCommand>();
        if (!(await validator.ValidateAsync(request, cancellationToken)).IsValid)
        {
            return PaymentNotFound.Result();
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetCurrentAccountOrderBeforeExpiryAsync(
            inventory, currentUser, tenant.TenantId, request.EventId, request.OrderId, timeProvider, cancellationToken);
        return order is null ? PaymentNotFound.Result() : await payments.StartAsync(order, request.Acceptance, cancellationToken);
    }
}

public sealed class RetryAuthenticatedRegistrationPaymentCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    RegistrationPaymentContractService payments)
    : IRequestHandler<RetryAuthenticatedRegistrationPaymentCommand, RegistrationPaymentCommandResultDto>
{
    public async Task<RegistrationPaymentCommandResultDto> Handle(RetryAuthenticatedRegistrationPaymentCommand request, CancellationToken cancellationToken)
    {
        var validator = new AuthenticatedRegistrationOrderAccessCommandValidator<RetryAuthenticatedRegistrationPaymentCommand>();
        if (!(await validator.ValidateAsync(request, cancellationToken)).IsValid)
        {
            return PaymentNotFound.Result();
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetCurrentAccountOrderBeforeExpiryAsync(
            inventory, currentUser, tenant.TenantId, request.EventId, request.OrderId, timeProvider, cancellationToken);
        return order is null ? PaymentNotFound.Result() : await payments.RetryAsync(order, cancellationToken);
    }
}

public sealed class RequestAuthenticatedRegistrationRefundCommandHandler(
    IRegistrationInventoryRepository inventory,
    IEventRepository events,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    RegistrationRefundService refunds)
    : IRequestHandler<RequestAuthenticatedRegistrationRefundCommand, RegistrationRefundCommandResultDto>
{
    public async Task<RegistrationRefundCommandResultDto> Handle(
        RequestAuthenticatedRegistrationRefundCommand request,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await new RegistrationRefundRequestDtoValidator()
            .ValidateAsync(request.Request, cancellationToken);
        if (!validation.IsValid || request.Request.ReasonCode != "event_cancelled")
        {
            return RefundCommandFailures.Invalid();
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
            inventory, currentUser, tenant.TenantId, request.EventId, request.OrderId, cancellationToken);
        Explore.Domain.Event? @event = order is null ? null : await events.GetById(request.EventId);
        if (order is null || @event is null || @event.TenantId != tenant.TenantId ||
            @event.EventStatusId != (int)EventStatusEnum.Cancelled || !currentUser.UserId.HasValue)
        {
            return RefundCommandFailures.NotFound();
        }

        return await refunds.InitiateAsync(
            order, request.Request.AmountMinor, request.IdempotencyKey, currentUser.UserId.Value,
            "buyer", request.Request.ReasonCode, cancellationToken);
    }
}

public sealed class CreateStudioRegistrationRefundCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    RegistrationRefundService refunds)
    : IRequestHandler<CreateStudioRegistrationRefundCommand, RegistrationRefundCommandResultDto>
{
    public async Task<RegistrationRefundCommandResultDto> Handle(
        CreateStudioRegistrationRefundCommand request,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await new RegistrationRefundRequestDtoValidator()
            .ValidateAsync(request.Request, cancellationToken);
        if (!validation.IsValid || !currentUser.UserId.HasValue)
        {
            return RefundCommandFailures.Invalid();
        }

        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(
            request.OrderId, tenant.TenantId, cancellationToken);
        if (order?.EventId != request.EventId)
        {
            return RefundCommandFailures.NotFound();
        }

        return await refunds.InitiateAsync(
            order, request.Request.AmountMinor, request.IdempotencyKey, currentUser.UserId.Value,
            "organizer", request.Request.ReasonCode, cancellationToken);
    }
}

public sealed class RespondAuthenticatedRegistrationMaterialChangeCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    RegistrationMaterialChangeChoiceService choices)
    : IRequestHandler<RespondAuthenticatedRegistrationMaterialChangeCommand, RegistrationMaterialChangeChoiceCommandResultDto>
{
    public async Task<RegistrationMaterialChangeChoiceCommandResultDto> Handle(
        RespondAuthenticatedRegistrationMaterialChangeCommand request,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await new RegistrationMaterialChangeChoiceRequestDtoValidator()
            .ValidateAsync(request.Request, cancellationToken);
        if (!validation.IsValid || !currentUser.UserId.HasValue)
        {
            return RegistrationMaterialChangeChoiceCommandResultDto.Failure(
                BaseCommandResponse.Failure<Guid>("material_change_choice_invalid", "Material-change choice is invalid."));
        }

        RegistrationOrder? order = await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
            inventory, currentUser, tenant.TenantId, request.EventId, request.OrderId, cancellationToken);
        return order is null
            ? RegistrationMaterialChangeChoiceCommandResultDto.Failure(
                BaseCommandResponse.Failure<Guid>("registration_order_not_found", "Registration order was not found."))
            : await choices.RespondAsync(
                order, request.Request.CampaignId, request.Request.ChoiceCode,
                currentUser.UserId.Value, cancellationToken);
    }
}

public sealed class RetryStudioRegistrationRefundCommandHandler(
    IRegistrationInventoryRepository inventory,
    IRefundAttemptRepository refunds,
    ITenantContext tenant,
    TimeProvider timeProvider)
    : IRequestHandler<RetryStudioRegistrationRefundCommand, RegistrationRefundCommandResultDto>
{
    public async Task<RegistrationRefundCommandResultDto> Handle(
        RetryStudioRegistrationRefundCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(
            request.OrderId, tenant.TenantId, cancellationToken);
        RefundAttempt? attempt = await refunds.GetByIdAsync(
            tenant.TenantId, request.RefundAttemptId, cancellationToken);
        if (order?.EventId != request.EventId || attempt?.RegistrationOrderId != request.OrderId ||
            attempt.SourceCampaignId is not null)
        {
            return RefundCommandFailures.NotFound();
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        bool retried = await refunds.RetryProviderBlockedAndScheduleAsync(
            attempt,
            RefundOutboxMessageFactory.CreateReconciliation(attempt, now, now),
            now,
            cancellationToken);
        return retried
            ? RegistrationRefundCommandResultDto.Success(
                attempt.Id, null, RegistrationPaymentContractService.MapRefund(attempt))
            : RefundCommandFailures.Invalid();
    }
}

file static class RefundCommandFailures
{
    public static RegistrationRefundCommandResultDto Invalid() => RegistrationRefundCommandResultDto.Failure(
        BaseCommandResponse.Failure<Guid>("refund_request_invalid", "Refund request is invalid."));

    public static RegistrationRefundCommandResultDto NotFound() => RegistrationRefundCommandResultDto.Failure(
        BaseCommandResponse.Failure<Guid>("registration_order_not_found", "Registration order was not found."));
}

file static class PaymentNotFound
{
    public static RegistrationPaymentCommandResultDto Result() => RegistrationPaymentCommandResultDto.Failure(
        BaseCommandResponse.Failure<Guid>("registration_order_not_found", "Registration order was not found."));
}
