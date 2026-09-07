using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record RegistrationFinalizationClaim(
    Guid EffectId,
    Guid TenantId,
    Guid RegistrationOrderId,
    Guid LeaseToken,
    long ProcessingFence);

public enum SucceededPaymentLookupStatus
{
    Missing,
    Found,
    Conflict
}

public sealed record SucceededPaymentLookupResult(
    SucceededPaymentLookupStatus Status,
    PaymentAttempt? Attempt,
    PaymentSucceededObservation? Observation,
    string? Code)
{
    public const string DuplicateCode = "payment_duplicate_succeeded_observations";

    public static SucceededPaymentLookupResult Missing() =>
        new(SucceededPaymentLookupStatus.Missing, null, null, null);

    public static SucceededPaymentLookupResult Found(
        PaymentAttempt attempt,
        PaymentSucceededObservation observation) =>
        new(SucceededPaymentLookupStatus.Found, attempt, observation, null);

    public static SucceededPaymentLookupResult Conflict() =>
        new(SucceededPaymentLookupStatus.Conflict, null, null, DuplicateCode);
}

public interface IRegistrationFinalizationRepository
{
    Task<bool> RecordFulfillmentAsync(
        RegistrationRequirementFulfillment fulfillment,
        DateTime recordedAt,
        CancellationToken cancellationToken);

    Task<bool> TryRecordSkippedFulfillmentsAndConsumeAttemptAsync(
        RegistrationAttempt attempt,
        Guid expectedAttemptConcurrencyStamp,
        IReadOnlyCollection<RegistrationRequirementFulfillment> fulfillments,
        DateTime recordedAt,
        CancellationToken cancellationToken);

    Task<bool> AreMandatoryRequirementsFulfilledAsync(
        Guid tenantId,
        Guid registrationOrderId,
        CancellationToken cancellationToken);

    Task<SucceededPaymentLookupResult> GetSucceededPaymentAsync(
        Guid tenantId,
        Guid registrationOrderId,
        CancellationToken cancellationToken);

    Task RequestAsync(
        RegistrationOrder order,
        DateTime requestedAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationRequirementFulfillment>> GetFulfillmentsAsync(
        Guid tenantId,
        Guid registrationOrderId,
        Guid registrationRequirementId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationFinalizationClaim>> ClaimDueAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        RegistrationFinalizationClaim claim,
        DateTime completedAt,
        CancellationToken cancellationToken);

    Task<bool> RetryAsync(
        RegistrationFinalizationClaim claim,
        DateTime nextAttemptAt,
        DateTime failedAt,
        CancellationToken cancellationToken);
}
