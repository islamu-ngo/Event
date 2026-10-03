using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Projects a bounded retirement hint from persisted reference, retention, and
/// exact-target facts. The hint grants no authority; command admission rescans
/// the same facts under its caller-owned fenced transaction.
/// </summary>
public interface IStorageObjectRetirementEligibilityReader
{
    Task<bool> CanRetireAsync(
        StorageObject storageObject,
        DateTime serverNowUtc,
        CancellationToken cancellationToken);
}
