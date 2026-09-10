
using Explore.Application.Models;

namespace Explore.Application.Contracts.Identity;

public enum LocalIdentityLifecycleDeliveryState { Pending = 0, Unknown = 1, Accepted = 2, Failed = 3 }

public interface ILocalIdentityLifecycleDeliveryStore
{
    Task<LocalIdentityLifecycleRequest?> ReadRequestAsync(Guid localSubjectId, Guid externalLoginId,
        LocalIdentityLifecyclePurpose purpose, string? proposedAddress, string? expectedSecurityStamp,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<LocalIdentityLifecyclePointer>> ReadPendingAsync(int maximumCount, CancellationToken cancellationToken);
    Task<IReadOnlyList<LocalIdentityLifecyclePointer>> ReadPendingSynchronizationAsync(int maximumCount, CancellationToken cancellationToken);
    // The caller owns the global SMTP setting lease. Rate reservations can be lost on crash, never duplicated sends.
    Task<bool> TryReserveGlobalSmtpAsync(int limitPerMinute, CancellationToken cancellationToken);
    Task<Guid?> TryAdmitAsync(LocalIdentityLifecyclePointer operation, CancellationToken cancellationToken);
    Task CompleteAsync(LocalIdentityLifecyclePointer operation, Guid attemptId, SmtpDeliveryOutcome outcome,
        CancellationToken cancellationToken);
}
