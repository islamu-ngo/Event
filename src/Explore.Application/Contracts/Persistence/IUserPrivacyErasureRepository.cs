using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IUserPrivacyErasureRepository
{
    /// <summary>
    /// Removes only the original reporter's confidential narrative ciphertext across every
    /// tenant inside the authority-verified serializable erasure settlement. The exact native
    /// saga must already exist; custody and recognition adoption are not erasure authority.
    /// </summary>
    Task EraseModerationCaseNarrativesAsync(
        PrivacyErasureIntent intent,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PrivacyErasureProviderCandidate>> GetProviderCandidatesAsync(
        Guid subjectId,
        CancellationToken cancellationToken);

    Task EraseIdentityEmailOwnershipAsync(
        Guid subjectId,
        CancellationToken cancellationToken);

    Task EraseProviderBackedLocalUserMetadataAsync(
        Guid subjectId,
        CancellationToken cancellationToken);

    Task EraseMembershipsAndPreferencesAsync(
        Guid subjectId,
        CancellationToken cancellationToken);

    Task EraseRegistrationAndLocalNotificationsAsync(
        Guid subjectId,
        CancellationToken cancellationToken);

    Task AnonymizeRetainedAuditEvidenceAsync(
        Guid subjectId,
        CancellationToken cancellationToken);
}

public sealed record PrivacyErasureProviderCandidate(
    PrivacyErasureProviderKind ProviderKind,
    PrivacyErasureProviderAction Action,
    Guid? TenantId,
    Guid TargetId,
    PrivacyErasureProviderLocatorKind LocatorKind,
    string Locator);
