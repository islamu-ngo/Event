using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Exact expected native original assessment coordinates, not a subject, binding, required
/// set, currentness assertion or authenticated prerequisite. Owners reread actual entities.
/// </summary>
public sealed record ModerationProfileSubjectConsentAssessment(
    ModerationProfileCollectionAssessmentOriginal Original,
    Guid SourceId,
    long ExpectedSourceRevision,
    long ExpectedSourceGeneration,
    Guid AdoptionId,
    long ExpectedAdoptionRevision,
    long ExpectedAdoptionGeneration);

/// <summary>
/// Explicit actual-self nomination of SubjectConsent only. ExpectedGeneration is this
/// subject/procedure consent slot's CAS, zero initially, not an assessment or policy sequence.
/// Requested end cannot extend any original observation, assessment or parent window.
/// </summary>
public sealed record GrantModerationProfileSubjectConsent(
    ModerationProfileSubjectConsentAssessment Assessment,
    Guid ConsentId,
    long ExpectedGeneration,
    DateTimeOffset ValidUntil);

/// <summary>
/// Exact historical terminal correction. Consent generation and current subject/procedure
/// slot CAS are distinct so an older original may be withdrawn after a later original.
/// No subject, provider, assessment or live-policy authority is supplied by the caller.
/// </summary>
public sealed record WithdrawModerationProfileSubjectConsent(
    Guid ConsentId,
    long ExpectedConsentRevision,
    long ExpectedConsentGeneration,
    long ExpectedGeneration);

/// <summary>
/// Private finite source-only metadata, not authenticated prerequisite or collection permission.
/// Both writes own the clean real privacy enrollment transaction through its outer commit.
/// </summary>
public interface IModerationProfileSubjectConsentOwner
{
    Task<ModerationProfileSubjectConsent?> GrantAsync(
        GrantModerationProfileSubjectConsent request, CancellationToken cancellationToken = default);

    Task<ModerationProfileSubjectConsent?> WithdrawAsync(
        WithdrawModerationProfileSubjectConsent request, CancellationToken cancellationToken = default);
}
