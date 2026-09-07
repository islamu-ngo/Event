using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public static class RegistrationPaymentPayability
{
    public static bool IsCurrentlyPayable(int statusId, long totalDueMinor, DateTime? expiresAt, DateTime now) =>
        statusId == (int)RegistrationOrderStatusEnum.AwaitingPayment &&
        totalDueMinor > 0 &&
        expiresAt is { } expiry && expiry > now;
}
