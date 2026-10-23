namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Claims only committed finalization identities using independently selected
/// Global authority. Order execution and settlement require ordinary tenant scope.
/// </summary>
public interface IRegistrationFinalizationQueueRepository
{
    Task<IReadOnlyList<RegistrationFinalizationClaim>> ClaimDueAsync(
        string leaseOwner,
        int batchSize,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);
}
