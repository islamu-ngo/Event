using Explore.Domain;

namespace Explore.Application.Contracts.Identity;

/// <summary>Reacquires persisted machine authority on the owning Runtime transaction.</summary>
public interface IMachineMutationCommitAuthority
{
    Task<Event?> GetAuthorizedSessionParentAsync(
        Guid tenantId, Guid eventId, string action, CancellationToken cancellationToken);

    Task<bool> AuthorizeFooterPatchAsync(
        Guid tenantId, Guid groupId, Guid? linkId, CancellationToken cancellationToken);

    Task<LocationRoom?> GetAuthorizedRoomForUpdateAsync(
        Guid tenantId, Guid roomId, Guid? destinationLocationId, CancellationToken cancellationToken);
}
