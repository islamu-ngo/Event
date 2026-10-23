using Explore.Domain;
using Explore.Application.Models.Storage;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Independent database custody; callers perform filesystem effects only after these operations commit.</summary>
public interface ILocalStorageQuarantineOwner
{
    const int MaximumClaims = 500;

    Task<bool> IsLocalOrphanAsync(Guid bindingId, string objectKey, CancellationToken cancellationToken);
    Task<LocalStorageQuarantine?> AdmitLocalQuarantineAsync(Guid bindingId, string objectKey,
        long observedSize, DateTime observedLastModifiedUtc, DateTime modifiedBeforeUtc,
        string admissionReason, CancellationToken cancellationToken);
    Task<IReadOnlyList<LocalStorageQuarantine>> ClaimLocalQuarantineAsync(
        int batchSize, TimeSpan lease, CancellationToken cancellationToken);
    /// <summary>Rechecks the exact original live claim and absence of competing physical custody without renewing it.</summary>
    Task<bool> IsLocalQuarantineClaimCurrentAsync(
        FileStorageQuarantineInput claim, CancellationToken cancellationToken);
    Task<bool> CompleteLocalQuarantineAsync(Guid id, Guid claimStamp,
        LocalStorageQuarantineOutcome outcome, CancellationToken cancellationToken);
    Task<bool> BlockLocalQuarantineAsync(Guid id, Guid claimStamp, CancellationToken cancellationToken);
    Task<bool> RetryLocalQuarantineAsync(Guid id, Guid claimStamp,
        DateTime nextAttemptAtUtc, CancellationToken cancellationToken);
}
