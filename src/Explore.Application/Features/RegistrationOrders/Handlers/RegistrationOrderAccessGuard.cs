using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Validators;
using Explore.Application.Responses;
using Explore.Domain;

namespace Explore.Application.Features.RegistrationOrders.Handlers;

internal static class RegistrationOrderAccessGuard
{
    public static async Task<RegistrationOrder?> GetGuestOrderAsync(
        IRegistrationInventoryRepository inventory,
        IGuestCapabilityTokenService capabilities,
        Guid tenantId,
        Guid eventId,
        Guid orderId,
        string? capabilityToken,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(orderId, tenantId, cancellationToken);
        if (order is null || order.EventId != eventId || order.GuestAccessTokenHash is null ||
            order.ExpiresAt is { } expiresAt && expiresAt <= timeProvider.GetUtcNow().UtcDateTime)
        {
            return null;
        }

        return HasGuestAccess(
            order,
            eventId,
            capabilityToken,
            capabilities,
            timeProvider)
            ? order
            : null;
    }

    internal static bool HasGuestAccess(
        RegistrationOrder order,
        Guid eventId,
        string? capabilityToken,
        IGuestCapabilityTokenService capabilities,
        TimeProvider timeProvider) =>
        order.EventId == eventId
        && order.GuestAccessTokenHash is not null
        && (order.ExpiresAt is null
            || order.ExpiresAt >
            timeProvider.GetUtcNow().UtcDateTime)
        && capabilities.Matches(
            capabilityToken,
            order.GuestAccessTokenHash);

    public static async Task<RegistrationOrder?> GetCurrentAccountOrderAsync(
        IRegistrationInventoryRepository inventory,
        ICurrentUserService currentUser,
        Guid tenantId,
        Guid orderId,
        CancellationToken cancellationToken) => await GetCurrentAccountOrderAsync(
        inventory,
        currentUser,
        tenantId,
        eventId: null,
        orderId,
        cancellationToken);

    public static async Task<RegistrationOrder?> GetCurrentAccountOrderAsync(
        IRegistrationInventoryRepository inventory,
        ICurrentUserService currentUser,
        Guid tenantId,
        Guid? eventId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
        {
            return null;
        }

        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(orderId, tenantId, cancellationToken);
        return order?.AccountUserId == userId && (!eventId.HasValue || order.EventId == eventId.Value) ? order : null;
    }

    public static async Task<RegistrationOrder?> GetCurrentAccountOrderBeforeExpiryAsync(
        IRegistrationInventoryRepository inventory,
        ICurrentUserService currentUser,
        Guid tenantId,
        Guid eventId,
        Guid orderId,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order = await GetCurrentAccountOrderAsync(
            inventory, currentUser, tenantId, eventId, orderId, cancellationToken);
        return order?.ExpiresAt is { } expiresAt && expiresAt > timeProvider.GetUtcNow().UtcDateTime
            ? order
            : null;
    }

    public static async Task<RegistrationOrderLifecycleResponseDto> ExecuteGuestAsync<TCommand>(
        TCommand request,
        IRegistrationInventoryRepository inventory,
        IGuestCapabilityTokenService capabilities,
        ITenantContext tenant,
        TimeProvider timeProvider,
        Func<Guid, Guid, CancellationToken, Task<RegistrationOrderLifecycleResponseDto>> action,
        CancellationToken cancellationToken)
        where TCommand : IGuestRegistrationOrderAccessCommand
    {
        if (!(await new GuestRegistrationOrderAccessCommandValidator<TCommand>()
                .ValidateAsync(request, cancellationToken)).IsValid ||
            await GetGuestOrderAsync(
                inventory,
                capabilities,
                tenant.TenantId,
                request.EventId,
                request.OrderId,
                request.CapabilityToken,
                timeProvider,
                cancellationToken) is null)
        {
            return NotFound(request.OrderId);
        }

        return await action(request.OrderId, tenant.TenantId, cancellationToken);
    }

    public static async Task<RegistrationOrderLifecycleResponseDto> ExecuteCurrentAccountAsync<TCommand>(
        TCommand request,
        IRegistrationInventoryRepository inventory,
        ITenantContext tenant,
        ICurrentUserService currentUser,
        Func<Guid, Guid, CancellationToken, Task<RegistrationOrderLifecycleResponseDto>> action,
        CancellationToken cancellationToken)
        where TCommand : IAuthenticatedRegistrationOrderAccessCommand
    {
        if (!(await new AuthenticatedRegistrationOrderAccessCommandValidator<TCommand>()
                .ValidateAsync(request, cancellationToken)).IsValid ||
            await GetCurrentAccountOrderAsync(
                inventory,
                currentUser,
                tenant.TenantId,
                request.EventId,
                request.OrderId,
                cancellationToken) is null)
        {
            return NotFound(request.OrderId);
        }

        return await action(request.OrderId, tenant.TenantId, cancellationToken);
    }

    public static RegistrationOrderLifecycleResponseDto NotFound(Guid orderId) =>
        RegistrationOrderLifecycleResponseDto.Failure(BaseCommandResponse.Failure<Guid>(
            "registration_order_not_found", "Registration order was not found.", id: orderId));

    public static BaseCommandResponse<Guid> ParticipantNotFound(Guid orderId) => BaseCommandResponse.Failure<Guid>(
        "registration_order_not_found", "Registration order was not found.", id: orderId);
}
