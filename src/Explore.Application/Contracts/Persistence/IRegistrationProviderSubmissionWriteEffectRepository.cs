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
    int AttemptCount,
    DateTime LeaseExpiresAt);

public sealed record RegistrationProviderSubmissionWriteDelivery(
    RegistrationOrder Order,
    RegistrationAttempt Attempt,
    RegistrationSubmission Submission,
    RegistrationProviderBinding Binding,
    IReadOnlyList<RegistrationAnswer> Answers,
    IReadOnlyList<RegistrationFormField> Fields);

public interface IRegistrationProviderSubmissionWriteEffectRepository
{
    Task<RegistrationProviderSubmissionWriteDelivery?> GetDeliveryAsync(
        RegistrationProviderSubmissionWriteClaim claim,
        CancellationToken cancellationToken);

    /// <summary>Reloads original custody and source lineage after preparation, immediately before sink handoff.</summary>
    Task<bool> IsDeliveryCurrentAsync(
        RegistrationProviderSubmissionWriteClaim claim,
        RegistrationProviderSubmissionWriteDelivery delivery,
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
