using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Promotions.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Handlers;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Promotions.Handlers.Commands;

public sealed class ApplyGuestPromotionCodeToRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    ISender sender)
    : IRequestHandler<ApplyGuestPromotionCodeToRegistrationOrderCommand, PromotionRedemptionResponseDto>
{
    public async Task<PromotionRedemptionResponseDto> Handle(
        ApplyGuestPromotionCodeToRegistrationOrderCommand request,
        CancellationToken cancellationToken) =>
        await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory,
            capabilities,
            tenant.TenantId,
            request.EventId,
            request.OrderId,
            request.CapabilityToken,
            timeProvider,
            cancellationToken) is null
            ? Unavailable(request.OrderId)
            : await sender.Send(new ApplyPromotionCodeToRegistrationOrderCommand(request.OrderId, request.Code), cancellationToken);

    private static PromotionRedemptionResponseDto Unavailable(Guid orderId) =>
        PromotionRedemptionAccessFailures.Unavailable(orderId);
}

public sealed class RemoveGuestPromotionFromRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    ISender sender)
    : IRequestHandler<RemoveGuestPromotionFromRegistrationOrderCommand, PromotionRedemptionResponseDto>
{
    public async Task<PromotionRedemptionResponseDto> Handle(
        RemoveGuestPromotionFromRegistrationOrderCommand request,
        CancellationToken cancellationToken) =>
        await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory,
            capabilities,
            tenant.TenantId,
            request.EventId,
            request.OrderId,
            request.CapabilityToken,
            timeProvider,
            cancellationToken) is null
            ? Unavailable(request.OrderId)
            : await sender.Send(new RemovePromotionFromRegistrationOrderCommand(request.OrderId), cancellationToken);

    private static PromotionRedemptionResponseDto Unavailable(Guid orderId) =>
        PromotionRedemptionAccessFailures.Unavailable(orderId);
}

public sealed class ApplyAuthenticatedPromotionCodeToRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    ISender sender)
    : IRequestHandler<ApplyAuthenticatedPromotionCodeToRegistrationOrderCommand, PromotionRedemptionResponseDto>
{
    public async Task<PromotionRedemptionResponseDto> Handle(
        ApplyAuthenticatedPromotionCodeToRegistrationOrderCommand request,
        CancellationToken cancellationToken) =>
        await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
            inventory,
            currentUser,
            tenant.TenantId,
            request.EventId,
            request.OrderId,
            cancellationToken) is null
            ? Unavailable(request.OrderId)
            : await sender.Send(new ApplyPromotionCodeToRegistrationOrderCommand(request.OrderId, request.Code), cancellationToken);

    private static PromotionRedemptionResponseDto Unavailable(Guid orderId) =>
        PromotionRedemptionAccessFailures.Unavailable(orderId);
}

public sealed class RemoveAuthenticatedPromotionFromRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    ISender sender)
    : IRequestHandler<RemoveAuthenticatedPromotionFromRegistrationOrderCommand, PromotionRedemptionResponseDto>
{
    public async Task<PromotionRedemptionResponseDto> Handle(
        RemoveAuthenticatedPromotionFromRegistrationOrderCommand request,
        CancellationToken cancellationToken) =>
        await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
            inventory,
            currentUser,
            tenant.TenantId,
            request.EventId,
            request.OrderId,
            cancellationToken) is null
            ? Unavailable(request.OrderId)
            : await sender.Send(new RemovePromotionFromRegistrationOrderCommand(request.OrderId), cancellationToken);

    private static PromotionRedemptionResponseDto Unavailable(Guid orderId) =>
        PromotionRedemptionAccessFailures.Unavailable(orderId);
}

file static class PromotionRedemptionAccessFailures
{
    public static PromotionRedemptionResponseDto Unavailable(Guid orderId) =>
        PromotionRedemptionResponseDto.Failure(BaseCommandResponse.Failure<Guid>(
            PromotionRedemptionFailureCodes.Unavailable,
            "Promotion cannot be changed for this order.",
            [PromotionRedemptionFailureCodes.Unavailable],
            orderId));
}
