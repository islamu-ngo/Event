using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Original incoming-effect custody, including the retained source identity observed before provider work.
/// Renewal does not authorize replacing this expiry snapshot.
/// </summary>
public sealed record RegistrationProviderSubmissionEffectCustody(
    IncomingWebhookEffectClaim Claim,
    DateTime LeaseExpiresAt,
    Guid IncomingWebhookMessageId,
    string Provider,
    string ProviderMessageId,
    string EffectKind,
    string SourceEventType,
    string PayloadSha256);

public interface IRegistrationProviderSubmissionEffectRepository
{
    Task<bool> HasCustodyAsync(
        RegistrationProviderSubmissionEffectCustody custody,
        CancellationToken cancellationToken);

    Task<RegistrationSubmissionPersistenceResult> PersistAcceptedAsync(
        RegistrationProviderSubmissionEffectCustody custody,
        RegistrationAttempt attempt,
        RegistrationSubmission submission,
        Guid expectedAttemptConcurrencyStamp,
        IReadOnlyCollection<RegistrationAnswer> answers,
        IReadOnlyCollection<RegistrationConsentRecord> consentRecords,
        IReadOnlyCollection<RegistrationSubmissionIssue> issues,
        IReadOnlyCollection<RegistrationRequirementFulfillment> fulfillments,
        CancellationToken cancellationToken);

    Task<RegistrationSubmissionPersistenceResult> PersistEvidenceAsync(
        RegistrationProviderSubmissionEffectCustody custody,
        RegistrationSubmission submission,
        IReadOnlyCollection<RegistrationSubmissionIssue> issues,
        CancellationToken cancellationToken);
}
