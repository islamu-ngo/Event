using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IUserLocationPrivacyErasureRepository
{
    /// <summary>Acquires the native subject fence before any erasure source write.</summary>
    Task FenceSubjectAsync(Guid subjectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Location>> GetOwnedPrivateHomesAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EventLocation>> GetEventLocationsAsync(
        IReadOnlyCollection<Guid> locationIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Actor>> GetUserActorsAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(
        IReadOnlyCollection<EventLocationDisclosureAudit> audits,
        CancellationToken cancellationToken);
}
