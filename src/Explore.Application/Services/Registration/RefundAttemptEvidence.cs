using Explore.Application.Contracts.Payments;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public static class RefundAttemptEvidence
{
    public static void Apply(
        RefundAttempt attempt,
        RefundProviderObservation observation,
        DateTime observedAt,
        string? providerRequestId)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(observation);
        if (string.IsNullOrWhiteSpace(observation.ProviderRefundId) ||
            observation.ProviderRefundId.Length > 200 ||
            observation.ProviderRefundId.Any(char.IsControl) ||
            !string.Equals(observation.ProviderPaymentId, attempt.ProviderPaymentId, StringComparison.Ordinal) ||
            observation.AmountMinor != attempt.Allocation.TotalMinor ||
            !string.Equals(observation.CurrencyCode, attempt.CurrencyCode, StringComparison.Ordinal) ||
            observation.ApplicationFeeRefundFailureCode is { Length: > 80 } ||
            observation.ApplicationFeeRefundFailureCode?.Any(char.IsControl) == true)
        {
            attempt.MarkUnknown(observedAt, providerRequestId, observation.ProviderRefundId);
            return;
        }

        switch (observation.Status)
        {
            case RefundProviderStatus.Pending:
                attempt.MarkPending(observation.ProviderRefundId, observedAt, providerRequestId);
                break;
            case RefundProviderStatus.RequiresAction:
                attempt.MarkRequiresAction(observation.ProviderRefundId, observedAt, providerRequestId);
                break;
            case RefundProviderStatus.Succeeded:
                bool providerBlocked = attempt.Status == RefundAttemptStatusEnum.RequiresAction &&
                                       attempt.FailureCode is not null;
                attempt.MarkBuyerRefundSucceeded(observation.ProviderRefundId, observedAt, providerRequestId);
                long expectedFeeRefund = checked(
                    attempt.Allocation.PlatformFeeMinor + attempt.Allocation.PlatformContributionMinor);
                if (observation.ApplicationFeeRefundAmountMinor == expectedFeeRefund)
                {
                    attempt.MarkSucceeded(
                        observation.ProviderRefundId,
                        observedAt,
                        providerRequestId,
                        expectedFeeRefund);
                }
                else if (!string.IsNullOrWhiteSpace(observation.ApplicationFeeRefundFailureCode))
                {
                    attempt.MarkProviderBlocked(
                        observedAt,
                        providerRequestId,
                        observation.ApplicationFeeRefundFailureCode);
                }
                else if (!providerBlocked)
                {
                    attempt.MarkUnknown(observedAt, providerRequestId, observation.ProviderRefundId);
                }
                break;
            case RefundProviderStatus.Failed:
                attempt.MarkFailed(observation.ProviderRefundId, observedAt, providerRequestId);
                break;
            case RefundProviderStatus.Cancelled:
                attempt.MarkCancelled(observation.ProviderRefundId, observedAt, providerRequestId);
                break;
            default:
                attempt.MarkUnknown(observedAt, providerRequestId);
                break;
        }
    }
}
