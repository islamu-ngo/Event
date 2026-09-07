using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public sealed record RegistrationProviderSubmissionWriteClaim(
    Guid EffectId,
    Guid TenantId,
    Guid RegistrationSubmissionId,
    Guid RegistrationAttemptId,
    Guid RegistrationProviderBindingId,
    Guid LeaseToken,
    long ProcessingFence,
    int AttemptCount);

public sealed record RegistrationProviderSubmissionWriteDelivery(
    RegistrationAttempt Attempt,
    RegistrationSubmission Submission,
    RegistrationProviderBinding Binding,
    IReadOnlyList<RegistrationAnswer> Answers,
    IReadOnlyList<RegistrationFormField> Fields);

public interface IRegistrationProviderSubmissionWriteEffectRepository
{
    Task<IReadOnlyList<RegistrationProviderSubmissionWriteClaim>> ClaimDueAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<RegistrationProviderSubmissionWriteDelivery?> GetDeliveryAsync(
        RegistrationProviderSubmissionWriteClaim claim,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        RegistrationProviderSubmissionWriteClaim claim,
        DateTime completedAt,
        CancellationToken cancellationToken);

    Task<bool> RetryAsync(
        RegistrationProviderSubmissionWriteClaim claim,
        string failureCode,
        DateTime nextAttemptAt,
        DateTime failedAt,
        CancellationToken cancellationToken);

    Task<bool> DeadLetterAsync(
        RegistrationProviderSubmissionWriteClaim claim,
        string failureCode,
        DateTime deadLetteredAt,
        CancellationToken cancellationToken);

    Task<bool> ParkAmbiguousAsync(
        RegistrationProviderSubmissionWriteClaim claim,
        string failureCode,
        DateTime parkedAt,
        CancellationToken cancellationToken);
}
