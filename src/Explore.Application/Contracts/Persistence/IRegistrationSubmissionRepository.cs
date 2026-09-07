using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IRegistrationSubmissionRepository
{
    Task PersistAttemptAsync(RegistrationAttempt attempt, CancellationToken cancellationToken);

    Task<bool> PersistReplacementAttemptAsync(
        RegistrationAttempt attempt,
        Guid supersededAttemptId,
        string supersessionReason,
        DateTime supersededAt,
        CancellationToken cancellationToken);

    Task<RegistrationSubmissionPersistenceResult> PersistAcceptedAsync(
        RegistrationAttempt attempt,
        RegistrationSubmission submission,
        Guid expectedAttemptConcurrencyStamp,
        CancellationToken cancellationToken);

    Task<RegistrationSubmissionPersistenceResult> PersistAcceptedWithNormalizationAsync(
        RegistrationAttempt attempt,
        RegistrationSubmission submission,
        Guid expectedAttemptConcurrencyStamp,
        IReadOnlyCollection<RegistrationAnswer> answers,
        IReadOnlyCollection<RegistrationConsentRecord> consentRecords,
        IReadOnlyCollection<RegistrationSubmissionIssue> issues,
        IReadOnlyCollection<RegistrationRequirementFulfillment> fulfillments,
        CancellationToken cancellationToken,
        RegistrationProviderSubmissionWriteEffect? providerWriteEffect = null);

    Task<RegistrationSubmissionPersistenceResult> PersistEvidenceOnlyAsync(
        RegistrationSubmission submission,
        CancellationToken cancellationToken);

    Task<RegistrationAttempt?> GetAttemptAsync(Guid tenantId, Guid attemptId, CancellationToken cancellationToken);

    Task<RegistrationSubmission?> GetSubmissionAsync(Guid tenantId, Guid submissionId, CancellationToken cancellationToken);

    Task<RegistrationRequirement?> GetRequirementAsync(
        Guid tenantId,
        Guid requirementId,
        CancellationToken cancellationToken);

    Task PersistNormalizationAsync(
        IReadOnlyCollection<RegistrationAnswer> answers,
        IReadOnlyCollection<RegistrationConsentRecord> consentRecords,
        IReadOnlyCollection<RegistrationSubmissionIssue> issues,
        CancellationToken cancellationToken);

    Task<bool> PersistRevisionAsync(
        RegistrationSubmission submission,
        RegistrationSubmissionRevision revision,
        Guid expectedSubmissionConcurrencyStamp,
        CancellationToken cancellationToken);

    Task<bool> PersistFinalizationAsync(
        RegistrationSubmission submission,
        Guid expectedSubmissionConcurrencyStamp,
        CancellationToken cancellationToken);
}

public enum RegistrationSubmissionPersistenceOutcome
{
    Inserted,
    Existing,
    EvidenceOnlyConflict,
    AttemptUnavailable
}

public sealed record RegistrationSubmissionPersistenceResult(
    RegistrationSubmissionPersistenceOutcome Outcome,
    RegistrationSubmission? Submission);
