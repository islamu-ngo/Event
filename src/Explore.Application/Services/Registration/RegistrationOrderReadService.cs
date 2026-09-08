using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationOrderReadService(
    IRegistrationInventoryRepository inventory,
    IPlatformContributionSettingRepository contributionSettings,
    IPaidOrderAcceptanceService paidAcceptance,
    TimeProvider timeProvider)
{
    public async Task<RegistrationOrderDto?> GetAsync(
        Guid orderId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(orderId, tenantId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        PlatformContributionSetting? contributionSetting = await contributionSettings.GetActiveAsync(cancellationToken);
        RegistrationOrderDto dto = RegistrationOrderDto.From(order, contributionSetting: contributionSetting);
        bool paidCheckoutActivationAvailable = RegistrationPaymentPayability.IsCurrentlyPayable(
                dto.StatusId,
                dto.TotalDueMinor,
                dto.ExpiresAt,
                timeProvider.GetUtcNow().UtcDateTime) &&
            (await paidAcceptance.DescribeAsync(order, cancellationToken)).Success;
        return dto with
        {
            PaidCheckoutActivationAvailable = paidCheckoutActivationAvailable,
        };
    }

    public async Task<IReadOnlyList<RegistrationOrderDto>> GetByEventAsync(
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RegistrationOrder> orders = await inventory.GetOrdersByEventAsync(
            eventId,
            tenantId,
            cancellationToken);
        return orders.Select(order => RegistrationOrderDto.From(order)).ToArray();
    }
}
