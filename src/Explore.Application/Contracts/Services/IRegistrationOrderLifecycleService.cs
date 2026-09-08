using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Contracts.Services;

public interface IRegistrationOrderLifecycleService
{
    Task<RegistrationOrderDto?> GetAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<RegistrationOrderLifecycleResponseDto> SubmitAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<RegistrationOrderLifecycleResponseDto> SubmitAsync(
        Guid orderId,
        Guid tenantId,
        int? platformContributionBasisPoints,
        CancellationToken cancellationToken);

    Task<RegistrationOrderLifecycleResponseDto> ReadyForCheckoutAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<RegistrationOrderLifecycleResponseDto> FinalizeFreeAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<RegistrationOrderLifecycleResponseDto> FinalizePaidAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<RegistrationOrderLifecycleResponseDto> CancelAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<RegistrationOrderLifecycleResponseDto> RecoverExpiredHoldAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<CheckoutDispatchConfigurationDisposition> CancelExpiredConfigurationBlockedPaymentAsync(
        CheckoutDispatchClaim claim,
        DateTime observedAt,
        CancellationToken cancellationToken);
}
