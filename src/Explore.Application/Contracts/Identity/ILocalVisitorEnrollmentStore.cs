using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Application.Contracts.Identity;

/// <summary>
/// Storage operations bound to the original selected Local identity/application
/// execution scope. Policy callbacks run inside that owner's retained final fences.
/// </summary>
public interface ILocalVisitorEnrollmentStore
{
    Guid TenantId { get; }
    Task<VisitorEnrollmentOperation> ReserveAsync(Guid operationId, string proofDigest,
        Func<CancellationToken, Task> authorizeNewEnrollment, CancellationToken cancellationToken);
    Task<VisitorEnrollmentOperation?> ReadAsync(Guid operationId, CancellationToken cancellationToken);
    Task<LocalCredentialProvisioningSnapshot?> PrepareAsync(Guid operationId, string username,
        string password, string? email, CancellationToken cancellationToken);
    Task<VisitorEnrollmentOperation?> RecordPreparedAsync(Guid operationId, CancellationToken cancellationToken);
    ITenantEnrollmentAdmissionWriter CreateAdmissionWriter(Guid operationId);
    Task<LocalCredentialActivationOutcome> ActivateAsync(Guid operationId, CancellationToken cancellationToken);
    Task<VisitorEnrollmentOperation?> AcknowledgeAsync(Guid operationId, CancellationToken cancellationToken);
    Task<VisitorEnrollmentOperation?> CancelAsync(
        Guid operationId, bool requireExpired, CancellationToken cancellationToken);
    Task<bool> DeleteCanceledAsync(Guid operationId, CancellationToken cancellationToken);
    Task<bool> RecoverAsync(VisitorEnrollmentRecoveryCandidate candidate, CancellationToken cancellationToken);
}
