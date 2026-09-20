using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Promotions.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Handlers;
using Explore.Application.Responses;

namespace Explore.Application.Features.Promotions.Handlers.Commands;

public sealed class ApplyGuestPromotionCodeToRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    ICommandHandler<ApplyPromotionCodeToRegistrationOrderCommand, PromotionRedemptionResponseDto> applyHandler)
    : ICommandHandler<ApplyGuestPromotionCodeToRegistrationOrderCommand, PromotionRedemptionResponseDto>
{
    public async Task<PromotionRedemptionResponseDto> ExecuteAsync(
        ApplyGuestPromotionCodeToRegistrationOrderCommand command,
        CancellationToken cancellationToken) =>
        await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory,
            capabilities,
            tenant.TenantId,
            command.EventId,
            command.OrderId,
            command.CapabilityToken,
            timeProvider,
            cancellationToken) is null
            ? Unavailable(command.OrderId)
            : await applyHandler.ExecuteAsync(new ApplyPromotionCodeToRegistrationOrderCommand(command.OrderId, command.Code), cancellationToken);

    private static PromotionRedemptionResponseDto Unavailable(Guid orderId) =>
        PromotionRedemptionAccessFailures.Unavailable(orderId);
}

public sealed class RemoveGuestPromotionFromRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    ICommandHandler<RemovePromotionFromRegistrationOrderCommand, PromotionRedemptionResponseDto> removeHandler)
    : ICommandHandler<RemoveGuestPromotionFromRegistrationOrderCommand, PromotionRedemptionResponseDto>
{
    public async Task<PromotionRedemptionResponseDto> ExecuteAsync(
        RemoveGuestPromotionFromRegistrationOrderCommand command,
        CancellationToken cancellationToken) =>
        await RegistrationOrderAccessGuard.GetGuestOrderAsync(
            inventory,
            capabilities,
            tenant.TenantId,
            command.EventId,
            command.OrderId,
            command.CapabilityToken,
            timeProvider,
            cancellationToken) is null
            ? Unavailable(command.OrderId)
            : await removeHandler.ExecuteAsync(new RemovePromotionFromRegistrationOrderCommand(command.OrderId), cancellationToken);

    private static PromotionRedemptionResponseDto Unavailable(Guid orderId) =>
        PromotionRedemptionAccessFailures.Unavailable(orderId);
}

public sealed class ApplyAuthenticatedPromotionCodeToRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    ICommandHandler<ApplyPromotionCodeToRegistrationOrderCommand, PromotionRedemptionResponseDto> applyHandler)
    : ICommandHandler<ApplyAuthenticatedPromotionCodeToRegistrationOrderCommand, PromotionRedemptionResponseDto>
{
    public async Task<PromotionRedemptionResponseDto> ExecuteAsync(
        ApplyAuthenticatedPromotionCodeToRegistrationOrderCommand command,
        CancellationToken cancellationToken) =>
        await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
            inventory,
            currentUser,
            tenant.TenantId,
            command.EventId,
            command.OrderId,
            cancellationToken) is null
            ? Unavailable(command.OrderId)
            : await applyHandler.ExecuteAsync(new ApplyPromotionCodeToRegistrationOrderCommand(command.OrderId, command.Code), cancellationToken);

    private static PromotionRedemptionResponseDto Unavailable(Guid orderId) =>
        PromotionRedemptionAccessFailures.Unavailable(orderId);
}

public sealed class RemoveAuthenticatedPromotionFromRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    ICommandHandler<RemovePromotionFromRegistrationOrderCommand, PromotionRedemptionResponseDto> removeHandler)
    : ICommandHandler<RemoveAuthenticatedPromotionFromRegistrationOrderCommand, PromotionRedemptionResponseDto>
{
    public async Task<PromotionRedemptionResponseDto> ExecuteAsync(
        RemoveAuthenticatedPromotionFromRegistrationOrderCommand command,
        CancellationToken cancellationToken) =>
        await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
            inventory,
            currentUser,
            tenant.TenantId,
            command.EventId,
            command.OrderId,
            cancellationToken) is null
            ? Unavailable(command.OrderId)
            : await removeHandler.ExecuteAsync(new RemovePromotionFromRegistrationOrderCommand(command.OrderId), cancellationToken);

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
