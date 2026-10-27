using Explore.Domain;
using Explore.Application.Services.Moderation;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Actual-self nomination only. Identity, original observation, selected ACTIVE policy,
/// procedure, parents and finite lifetime are resolved exclusively by the native owner.
/// Contains no subject ID, provider binding, age assertion or applicability verdict.
/// </summary>
public sealed record NominateModerationProfileCollectionSubjectObservation(Guid ProfileId, ModerationNativeMode Mode);

public interface IModerationProfileCollectionSubjectObservationOwner
{
    /// <summary>
    /// Persists original existing-Human metadata through the actual privacy identity commit.
    /// Returns an entity, never an assessment, consent, collection prerequisite or receipt.
    /// </summary>
    Task<ModerationProfileCollectionSubjectObservation?> ObserveAsync(
        NominateModerationProfileCollectionSubjectObservation nomination,
        CancellationToken cancellationToken = default);
}
