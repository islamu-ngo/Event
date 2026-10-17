using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IUserLocationPrivacyErasureRepository
{
    /// <summary>The PostgreSQL execute-only subject capability; other providers use entity persistence.</summary>
    ISubjectPrivacyErasureSettlement? NativeSettlement { get; }

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

    /// <summary>Retains portable-provider effects in the same original erasure transaction.</summary>
    Task RetainCorrectionReceiptsAsync(
        IReadOnlyCollection<LocationPrivacyCorrectionReceipt> receipts,
        CancellationToken cancellationToken);
}
