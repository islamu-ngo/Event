using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Services.Registration;

public sealed record PaidCheckoutActivationRequest(
    Guid TenantId,
    Guid EventId,
    string CurrencyCode,
    long CandidateAmountMinor,
    DateTime EvaluatedAt,
    Guid? ReservedPaymentAttemptId = null);

public sealed record PaidCheckoutActivationResult(
    bool IsActive,
    string? FailureCode,
    string Message,
    Guid? OrganizerActorId = null,
    Guid? EffectivePolicyVersionId = null,
    PaidCheckoutReservedExposure? ReservedExposure = null)
{
    public static PaidCheckoutActivationResult Failure(string code, string message) => new(false, code, message);
}

public interface IPaidCheckoutActivationService
{
    Task<PaidCheckoutActivationResult> EvaluateSaleControlAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task<PaidCheckoutActivationResult> EvaluateAsync(
        PaidCheckoutActivationRequest request,
        CancellationToken cancellationToken);
}

public sealed class PaidCheckoutActivationService(
    IPaidCheckoutActivationRepository repository,
    IPaidEventPolicyRepository policies,
    IEventRepository events,
    ITenantDirectoryOperatorReadinessEvaluator directoryOperatorReadiness,
    IPaidCheckoutGovernance governance) : IPaidCheckoutActivationService
{
    private readonly ITenantDirectoryOperatorReadinessEvaluator _directoryOperatorReadiness =
        directoryOperatorReadiness;

    public async Task<PaidCheckoutActivationResult> EvaluateSaleControlAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        TenantDirectoryOperatorReadinessAssessment directoryIdentity =
            await _directoryOperatorReadiness.EvaluateAsync(
                tenantId,
                TenantDirectoryOperatorIdentityCapability.PaidCommerce,
                cancellationToken);
        if (!directoryIdentity.IsReady)
        {
            return PaidCheckoutActivationResult.Failure(
                "tenant_directory_operator_identity_unavailable",
                "Tenant directory operator identity is unavailable for paid commerce.");
        }

        if (!governance.IsConfigured || !governance.IsActivated)
        {
            return PaidCheckoutActivationResult.Failure("payment_operator_inactive", "The instance operator has not activated new paid sales.");
        }
        PaidCheckoutSaleControl? global = await repository.GetSaleControlAsync(tenantId, null, false, cancellationToken);
        if (global is null)
        {
            return PaidCheckoutActivationResult.Failure(
                "paid_sale_control_uninitialized", "The tenant-wide paid-sale control has not been activated.");
        }
        PaidCheckoutSaleControl? eventControl = await repository.GetSaleControlAsync(tenantId, eventId, false, cancellationToken);
        return global.IsStopped || eventControl?.IsStopped == true
            ? PaidCheckoutActivationResult.Failure("paid_sale_stopped", "New paid sales are temporarily stopped.")
            : new PaidCheckoutActivationResult(true, null, "Paid sales are active from durable sale controls.");
    }

    public async Task<PaidCheckoutActivationResult> EvaluateAsync(
        PaidCheckoutActivationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.EventId == Guid.Empty || request.CandidateAmountMinor <= 0 ||
            request.EvaluatedAt == default || request.EvaluatedAt.Kind != DateTimeKind.Utc)
        {
            return PaidCheckoutActivationResult.Failure("payment_activation_invalid", "Paid Checkout activation facts are invalid.");
        }
        PaidCheckoutActivationResult saleControl = await EvaluateSaleControlAsync(
            request.TenantId, request.EventId, cancellationToken);
        if (!saleControl.IsActive)
        {
            return saleControl;
        }

        Event? eventTarget = await events.GetEventWithDetailsAsync(
            request.EventId,
            request.TenantId,
            cancellationToken);
        if (eventTarget?.TenantId != request.TenantId || eventTarget.OrganizerActorId is not Guid organizerActorId)
        {
            return PaidCheckoutActivationResult.Failure("payment_organizer_unavailable", "The event organizer is unavailable.");
        }

        PaidEventPolicyVersion? instancePolicy = await policies.GetActiveInstanceAsync(cancellationToken);
        PaidEventPolicyVersion? tenantPolicy = await policies.GetActiveTenantAsync(request.TenantId, cancellationToken);
        if (instancePolicy is null || instancePolicy.TenantId is not null || !instancePolicy.IsActive || !instancePolicy.IsPaymentsEnabled)
        {
            return PaidCheckoutActivationResult.Failure("payment_policy_unavailable", "The active paid-event policy is unavailable.");
        }
        if (tenantPolicy is not null)
        {
            try
            {
                PaidEventPolicyRules.ValidateTenantPolicy(instancePolicy, tenantPolicy);
            }
            catch (InvalidOperationException)
            {
                return PaidCheckoutActivationResult.Failure("payment_policy_invalid", "The effective paid-event policy is invalid.");
            }
            if (!tenantPolicy.IsActive || !tenantPolicy.IsPaymentsEnabled)
            {
                return PaidCheckoutActivationResult.Failure("payment_policy_unavailable", "The active paid-event policy is unavailable.");
            }
        }

        PaidEventPolicyVersion effectivePolicy = tenantPolicy ?? instancePolicy;
        if (!effectivePolicy.AllowedCurrencyCodes.Contains(request.CurrencyCode, StringComparer.Ordinal))
        {
            return PaidCheckoutActivationResult.Failure("payment_currency_unsupported", "The order currency is not enabled.");
        }

        PaidEventPolicyCurrencyRiskLimit? limit = effectivePolicy.CurrencyRiskLimits.SingleOrDefault(value =>
            string.Equals(value.CurrencyCode, request.CurrencyCode, StringComparison.Ordinal));
        DateTime? windowStart = limit?.RollingOrganizerWindowDays is { } days
            ? request.EvaluatedAt.AddDays(-days)
            : null;
        PaidCheckoutReservedExposure exposure = limit is null
            ? new(request.CurrencyCode, 0, 0, 0, 0)
            : await repository.GetReservedExposureAsync(
                request.TenantId, request.EventId, organizerActorId, request.CurrencyCode, windowStart,
                request.ReservedPaymentAttemptId, cancellationToken);
        if (limit?.WouldExceed(exposure, request.CandidateAmountMinor) == true)
        {
            return PaidCheckoutActivationResult.Failure(
                "payment_ceiling_exceeded", "The configured paid-sales ceiling would be exceeded.");
        }

        if (effectivePolicy.RequiresFirstPaidEventReview &&
            !await repository.HasPriorSucceededPaymentAsync(request.TenantId, organizerActorId, cancellationToken) &&
            !await repository.HasApprovalAsync(
                request.TenantId, request.EventId, organizerActorId, effectivePolicy.Id, request.CurrencyCode,
                PaidCheckoutReviewTrigger.FirstPaidEvent, request.CandidateAmountMinor, cancellationToken))
        {
            return PaidCheckoutActivationResult.Failure("payment_review_required", "Independent first-event review approval is required.");
        }
        if (limit?.HighValueReviewThresholdMinor is { } threshold && request.CandidateAmountMinor >= threshold &&
            !await repository.HasApprovalAsync(
                request.TenantId, request.EventId, organizerActorId, effectivePolicy.Id, request.CurrencyCode,
                PaidCheckoutReviewTrigger.HighValue, request.CandidateAmountMinor, cancellationToken))
        {
            return PaidCheckoutActivationResult.Failure("payment_review_required", "Independent high-value review approval is required.");
        }

        return new(true, null, "Paid Checkout is active from current authoritative facts.", organizerActorId, effectivePolicy.Id, exposure);
    }
}
