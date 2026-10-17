using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record RegistrationFinalizationClaim(
    Guid EffectId,
    Guid TenantId,
    Guid RegistrationOrderId,
    Guid LeaseToken,
    long ProcessingFence);

public sealed class RegistrationFinalizationCustodyLostException()
    : Exception("Registration finalization execution custody is no longer current.");

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
    /// <summary>
    /// Locks the original effect until the caller's transaction ends and checks
    /// its persisted tenant, order, token, fence, status and current expiry.
    /// Call again after awaited work, before returning from that transaction.
    /// </summary>
    Task<bool> TryAcquireExecutionAsync(
        RegistrationFinalizationClaim claim,
        CancellationToken cancellationToken);

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
