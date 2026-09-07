using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IUserPrivacyErasureRepository
{
    Task<IReadOnlyList<PrivacyErasureProviderCandidate>> GetProviderCandidatesAsync(
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
